using System.Text;
using Microsoft.EntityFrameworkCore;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของ RosterEndpoints (WP3-1, 2026-10-31) - Roster Manager (WP3-2) เรียกไฟล์นี้ทั้งหมด ----
record CreateStudentRequest(string StudentCode, string FullName, string? SchoolEmail, string ClassName, string? Password, bool ForceChangePasswordOnFirstLogin = true);
record UpdateStudentRequest(string StudentCode, string FullName, string? SchoolEmail, string ClassName);
record MoveStudentClassRequest(string ClassName);

/// <summary>ตรงกับ ImportDuplicatePolicy.UpdateExisting(0)/SkipExisting(1) ด้านล่าง</summary>
record ImportStudentsRequest(
    string CsvText,
    string StudentCodeColumn,
    string FullNameColumn,
    string? EmailColumn,
    string ClassNameColumn,
    string? BirthDateColumn,
    int DuplicatePolicy,
    int PasswordPolicy,
    string? FixedPassword,
    bool ForceChangePasswordOnFirstLogin = true);

record PromoteMappingItem(string FromClassName, string? ToClassName);
record PromoteStudentsRequest(List<PromoteMappingItem> Mappings, int GraduateAction);

/// <summary>ตรงกับ nf-import ขวาสุด (ถ้ามีรหัสนักเรียนซ้ำกับในระบบ)</summary>
internal enum ImportDuplicatePolicy { UpdateExisting, SkipExisting }

/// <summary>ตรงกับ nf-password สามตัวเลือก</summary>
internal enum ImportPasswordPolicy { Random6Digit, BirthDate, SameForAll }

/// <summary>ตรงกับ nf-promote "นักเรียนที่จบ"</summary>
internal enum GraduateAction { Disable, Delete }

/// <summary>
/// CRUD บัญชีนักเรียน + นำเข้า CSV (dry-run/commit) + รีเซ็ตรหัส/ปลดล็อก/ย้ายห้อง + เลื่อนชั้นปี
/// (WP3-1, 2026-10-31) - เป็น backend ของ Roster Manager (WP3-2, โปรแกรม WPF แยก) ตรงตาม wireframe
/// nf-roster, nf-import, nf-password, nf-print (ข้อมูลรหัสเท่านั้น - ตัวพิมพ์ PDF เป็นงานของ WP3-2),
/// nf-promote
///
/// สิทธิ์: Admin เท่านั้นสำหรับ CRUD/นำเข้า/เลื่อนชั้น ยกเว้นรีเซ็ตรหัสผ่าน+ปลดล็อกที่ Teacher ทำได้ถ้า
/// SignInSettings.TeacherCanResetPassword เปิดอยู่ (ดู docs/roadmap.md หัวข้อ Sign-in "สิทธิ์: Admin
/// เท่านั้น ยกเว้นรีเซ็ตรหัสและปลดล็อก")
/// </summary>
public static class RosterEndpoints
{
    public static WebApplication MapRosterEndpoints(this WebApplication app)
    {
        // ---- รายชื่อ + ค้นหา/กรอง (nf-roster) - Admin+Teacher อ่านได้ทั้งคู่ (Teacher ต้องหาคนก่อนจะรีเซ็ต/ปลดล็อกได้) ----
        app.MapGet("/api/students", async (HttpRequest request, AppDbContext db, string? className, string? search, string? status) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var query = db.Students.AsQueryable();
            if (!string.IsNullOrWhiteSpace(className)) query = query.Where(s => s.ClassName == className);
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(s => s.FullName.Contains(search) || s.StudentCode.Contains(search) || (s.SchoolEmail != null && s.SchoolEmail.Contains(search)));
            }
            query = status switch
            {
                "locked" => query.Where(s => s.IsLocked),
                "mustchange" => query.Where(s => s.MustChangePassword),
                "disabled" => query.Where(s => s.IsDisabled),
                "normal" => query.Where(s => !s.IsLocked && !s.MustChangePassword && !s.IsDisabled),
                _ => query
            };

            var students = await query.OrderBy(s => s.ClassName).ThenBy(s => s.StudentCode).ToListAsync();
            return Results.Ok(students.Select(ToPublicDto));
        });

        app.MapGet("/api/students/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var student = await db.Students.FindAsync(id);
            return student is null ? Results.NotFound() : Results.Ok(ToPublicDto(student));
        });

        // ---- เพิ่มนักเรียนทีละคน (ปุ่ม "เพิ่มนักเรียน…" บน nf-roster) ----
        app.MapPost("/api/students", async (CreateStudentRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var code = req.StudentCode?.Trim();
            var name = req.FullName?.Trim();
            var className = req.ClassName?.Trim();
            var email = string.IsNullOrWhiteSpace(req.SchoolEmail) ? null : req.SchoolEmail.Trim();

            if (string.IsNullOrEmpty(code)) return Results.BadRequest(new { error = "ต้องระบุรหัสนักเรียน" });
            if (string.IsNullOrEmpty(name)) return Results.BadRequest(new { error = "ต้องระบุชื่อ-สกุล" });
            if (string.IsNullOrEmpty(className)) return Results.BadRequest(new { error = "ต้องระบุชั้น/ห้องเรียน" });

            if (await db.Students.AnyAsync(s => s.StudentCode == code))
                return Results.Json(new { error = $"มีรหัสนักเรียน '{code}' อยู่แล้ว" }, statusCode: 409);
            if (email is not null && await db.Students.AnyAsync(s => s.SchoolEmail == email))
                return Results.Json(new { error = $"มีเมล '{email}' อยู่แล้ว" }, statusCode: 409);

            var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            var plainPassword = string.IsNullOrWhiteSpace(req.Password) ? StudentPasswordGenerator.GenerateRandomSixDigit() : req.Password;
            if (plainPassword.Length < settings.PasswordMinLength)
                return Results.BadRequest(new { error = $"รหัสผ่านต้องมีอย่างน้อย {settings.PasswordMinLength} ตัว" });

            var salt = PasswordHasher.GenerateSalt();
            var student = new Student
            {
                StudentCode = code,
                FullName = name,
                SchoolEmail = email,
                ClassName = className,
                PasswordSalt = salt,
                PasswordHash = PasswordHasher.HashPassword(plainPassword, salt),
                MustChangePassword = req.ForceChangePasswordOnFirstLogin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Students.Add(student);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentCreated", $"{admin.Username} เพิ่มนักเรียน '{student.StudentCode}' ({student.FullName})");

            return Results.Created($"/api/students/{student.Id}", new { student = ToPublicDto(student), generatedPassword = plainPassword });
        });

        app.MapPut("/api/students/{id:int}", async (int id, UpdateStudentRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var student = await db.Students.FindAsync(id);
            if (student is null) return Results.NotFound();

            var code = req.StudentCode?.Trim();
            var name = req.FullName?.Trim();
            var className = req.ClassName?.Trim();
            var email = string.IsNullOrWhiteSpace(req.SchoolEmail) ? null : req.SchoolEmail.Trim();

            if (string.IsNullOrEmpty(code)) return Results.BadRequest(new { error = "ต้องระบุรหัสนักเรียน" });
            if (string.IsNullOrEmpty(name)) return Results.BadRequest(new { error = "ต้องระบุชื่อ-สกุล" });
            if (string.IsNullOrEmpty(className)) return Results.BadRequest(new { error = "ต้องระบุชั้น/ห้องเรียน" });

            if (await db.Students.AnyAsync(s => s.Id != id && s.StudentCode == code))
                return Results.Json(new { error = $"มีรหัสนักเรียน '{code}' อยู่แล้ว" }, statusCode: 409);
            if (email is not null && await db.Students.AnyAsync(s => s.Id != id && s.SchoolEmail == email))
                return Results.Json(new { error = $"มีเมล '{email}' อยู่แล้ว" }, statusCode: 409);

            student.StudentCode = code;
            student.FullName = name;
            student.ClassName = className;
            student.SchoolEmail = email;
            student.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentEdited", $"{admin.Username} แก้ข้อมูลนักเรียน '{student.StudentCode}' ({student.FullName})");

            return Results.Ok(ToPublicDto(student));
        });

        // ลบบัญชีถาวร (รวมประวัติ) - ใช้น้อย ปิดใช้งาน (Disable) เป็นทางเลือกปกติกว่า
        app.MapDelete("/api/students/{id:int}", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var student = await db.Students.FindAsync(id);
            if (student is null) return Results.NotFound();

            var sessions = await db.StudentSessions.Where(s => s.StudentId == id).ToListAsync();
            db.StudentSessions.RemoveRange(sessions);
            db.Students.Remove(student);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentDeleted",
                $"{admin.Username} ลบนักเรียน '{student.StudentCode}' ({student.FullName}) ออกจากระบบถาวร ({sessions.Count} ประวัติการใช้เครื่องถูกลบด้วย)");

            return Results.Ok(new { message = "Student deleted." });
        });

        app.MapPost("/api/students/{id:int}/disable", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
            await SetDisabledAsync(id, true, request, db, activityLog));

        app.MapPost("/api/students/{id:int}/enable", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
            await SetDisabledAsync(id, false, request, db, activityLog));

        app.MapPut("/api/students/{id:int}/class", async (int id, MoveStudentClassRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var student = await db.Students.FindAsync(id);
            if (student is null) return Results.NotFound();

            var className = req.ClassName?.Trim();
            if (string.IsNullOrEmpty(className)) return Results.BadRequest(new { error = "ต้องระบุชั้น/ห้องเรียน" });

            var oldClass = student.ClassName;
            student.ClassName = className;
            student.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentClassChanged",
                $"{admin.Username} ย้ายนักเรียน '{student.StudentCode}' จาก '{oldClass}' ไป '{student.ClassName}'");

            return Results.Ok(ToPublicDto(student));
        });

        // ---- รีเซ็ตรหัสผ่าน / ปลดล็อก - Admin หรือ Teacher (ถ้าเปิดสิทธิ์) ----
        app.MapPost("/api/students/{id:int}/reset-password", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            if (RequireAdminOrDelegatedTeacher(admin, settings) is { } forbidden) return forbidden;

            var student = await db.Students.FindAsync(id);
            if (student is null) return Results.NotFound();

            var plainPassword = StudentPasswordGenerator.GenerateRandomSixDigit();
            var salt = PasswordHasher.GenerateSalt();
            student.PasswordSalt = salt;
            student.PasswordHash = PasswordHasher.HashPassword(plainPassword, salt);
            student.MustChangePassword = true;
            student.FailedLoginCount = 0;
            student.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentPasswordReset",
                $"{admin.Username} รีเซ็ตรหัสผ่านของนักเรียน '{student.StudentCode}' ({student.FullName})");

            return Results.Ok(new { message = "Password reset.", generatedPassword = plainPassword });
        });

        app.MapPost("/api/students/{id:int}/unlock", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
            if (RequireAdminOrDelegatedTeacher(admin, settings) is { } forbidden) return forbidden;

            var student = await db.Students.FindAsync(id);
            if (student is null) return Results.NotFound();

            student.IsLocked = false;
            student.FailedLoginCount = 0;
            student.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentUnlocked",
                $"{admin.Username} ปลดล็อกบัญชีนักเรียน '{student.StudentCode}' ({student.FullName})");

            return Results.Ok(ToPublicDto(student));
        });

        // ---- นำเข้า CSV (nf-import, nf-password) - Admin เท่านั้น ----
        app.MapPost("/api/students/import/dry-run", async (ImportStudentsRequest req, HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var result = await ProcessImportAsync(req, db, commit: false);
            return result.Match(ok => Results.Ok(ok), err => Results.BadRequest(new { error = err }));
        });

        app.MapPost("/api/students/import/commit", async (ImportStudentsRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var result = await ProcessImportAsync(req, db, commit: true);
            if (result.IsError)
            {
                return Results.BadRequest(new { error = result.ErrorMessage });
            }

            await activityLog.LogAdminActionAsync(admin, "StudentsImported",
                $"{admin.Username} นำเข้ารายชื่อนักเรียนจาก CSV: เพิ่มใหม่ {result.Value!.NewCount} คน, อัปเดต {result.Value.DuplicateUpdateCount} คน, ข้าม {result.Value.DuplicateSkipCount} คน, มีปัญหา {result.Value.ErrorCount} แถว");

            return Results.Ok(result.Value);
        });

        // ---- เลื่อนชั้นปีการศึกษาใหม่ (nf-promote) - Admin เท่านั้น, สำรอง DB อัตโนมัติก่อนเสมอ ----
        app.MapGet("/api/students/promote/preview", async (HttpRequest request, AppDbContext db) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            var counts = await db.Students
                .Where(s => !s.IsDisabled)
                .GroupBy(s => s.ClassName)
                .Select(g => new { ClassName = g.Key, Count = g.Count() })
                .OrderBy(x => x.ClassName)
                .ToListAsync();

            return Results.Ok(counts);
        });

        app.MapPost("/api/students/promote", async (PromoteStudentsRequest req, HttpRequest request, AppDbContext db, DatabaseBackupService backupService, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null) return Results.Unauthorized();
            if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
            if (RequireAdmin(admin) is { } forbidden) return forbidden;

            if (req.Mappings is null || req.Mappings.Count == 0)
                return Results.BadRequest(new { error = "ต้องระบุการเลื่อนชั้นอย่างน้อย 1 รายการ" });
            if (!Enum.IsDefined(typeof(GraduateAction), req.GraduateAction))
                return Results.BadRequest(new { error = "GraduateAction ไม่ถูกต้อง" });

            // สำรอง DB ก่อนเสมอ (docs/roadmap.md "เลื่อนชั้นปี...สำรอง DB ให้อัตโนมัติก่อนเลื่อน") - ถ้า
            // สำรองไม่สำเร็จ ต้องหยุดทันที ไม่แก้ข้อมูลใดๆ เลย (fail-safe ดีกว่าเลื่อนชั้นไปแล้วไม่มีทางถอย)
            DatabaseBackupService.BackupResult backup;
            try
            {
                backup = await backupService.BackupNowAsync();
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = $"สำรอง DB ก่อนเลื่อนชั้นไม่สำเร็จ - ยกเลิกการเลื่อนชั้น ({ex.Message})" }, statusCode: 500);
            }

            var promotedCount = 0;
            var graduatedCount = 0;
            var graduateAction = (GraduateAction)req.GraduateAction;

            foreach (var mapping in req.Mappings)
            {
                var fromClass = mapping.FromClassName?.Trim();
                if (string.IsNullOrEmpty(fromClass)) continue;

                var studentsInClass = await db.Students.Where(s => s.ClassName == fromClass && !s.IsDisabled).ToListAsync();
                var toClass = mapping.ToClassName?.Trim();

                if (string.IsNullOrEmpty(toClass))
                {
                    // จบการศึกษา
                    foreach (var student in studentsInClass)
                    {
                        if (graduateAction == GraduateAction.Delete)
                        {
                            var sessions = await db.StudentSessions.Where(s => s.StudentId == student.Id).ToListAsync();
                            db.StudentSessions.RemoveRange(sessions);
                            db.Students.Remove(student);
                        }
                        else
                        {
                            student.IsDisabled = true;
                            student.UpdatedAt = DateTime.UtcNow;
                        }
                        graduatedCount++;
                    }
                }
                else
                {
                    foreach (var student in studentsInClass)
                    {
                        student.ClassName = toClass;
                        student.UpdatedAt = DateTime.UtcNow;
                        promotedCount++;
                    }
                }
            }

            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "StudentsPromoted",
                $"{admin.Username} เลื่อนชั้นปีการศึกษาใหม่: เลื่อนชั้น {promotedCount} คน, จบการศึกษา {graduatedCount} คน ({graduateAction}) - สำรอง DB ไว้ที่ {backup.MainFileName}");

            return Results.Ok(new { promotedCount, graduatedCount, backupMainFileName = backup.MainFileName, backupActivityLogFileName = backup.ActivityLogFileName });
        });

        return app;
    }

    private static async Task<IResult> SetDisabledAsync(int id, bool disabled, HttpRequest request, AppDbContext db, IActivityLogService activityLog)
    {
        var admin = await GetAuthorizedAdmin(request, db);
        if (admin is null) return Results.Unauthorized();
        if (admin.HasDefaultPassword) return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);
        if (RequireAdmin(admin) is { } forbidden) return forbidden;

        var student = await db.Students.FindAsync(id);
        if (student is null) return Results.NotFound();

        student.IsDisabled = disabled;
        student.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await activityLog.LogAdminActionAsync(admin, disabled ? "StudentDisabled" : "StudentEnabled",
            $"{admin.Username} {(disabled ? "ปิด" : "เปิด")}ใช้งานบัญชีนักเรียน '{student.StudentCode}' ({student.FullName})");

        return Results.Ok(ToPublicDto(student));
    }

    /// <summary>ไม่ส่ง PasswordHash/PasswordSalt กลับไปเด็ดขาด (ดูเฉพาะ hash ของ password ก็ยังไม่ควรเดินทางออก API)</summary>
    private static object ToPublicDto(Student s) => new
    {
        s.Id,
        s.StudentCode,
        s.FullName,
        s.SchoolEmail,
        s.ClassName,
        s.MustChangePassword,
        s.FailedLoginCount,
        s.IsLocked,
        s.IsDisabled,
        s.CreatedAt,
        s.UpdatedAt,
        s.LastLoginAt
    };

    /// <summary>Admin ผ่านเสมอ - Teacher ผ่านได้เฉพาะตอน SignInSettings.TeacherCanResetPassword เปิดอยู่
    /// (ใช้กับ reset-password และ unlock เท่านั้น - endpoint อื่นทั้งหมดของไฟล์นี้ใช้ RequireAdmin ตรงๆ)</summary>
    private static IResult? RequireAdminOrDelegatedTeacher(AdminUser admin, SignInSettings settings) =>
        admin.Role == AdminRole.Admin || settings.TeacherCanResetPassword
            ? null
            : Results.Json(new { error = "Teacher accounts do not have permission to perform this action." }, statusCode: StatusCodes.Status403Forbidden);

    // ================== CSV import ==================

    private sealed record ImportRowResult(int RowNumber, string StudentCode, string FullName, string? Email, string ClassName, string Result, string? ErrorMessage, string? GeneratedPassword);
    private sealed record ImportSummary(int TotalRows, int NewCount, int DuplicateUpdateCount, int DuplicateSkipCount, int ErrorCount, List<ImportRowResult> Rows);

    /// <summary>คู่ (success, error) แทนการ throw - dry-run/commit ทั้งคู่ใช้ร่วมกัน</summary>
    private readonly record struct ImportOutcome(ImportSummary? Value, string? ErrorMessage)
    {
        public bool IsError => ErrorMessage is not null;
        public IResult Match(Func<ImportSummary, IResult> ok, Func<string, IResult> err) =>
            IsError ? err(ErrorMessage!) : ok(Value!);
    }

    /// <summary>
    /// ประมวลผล CSV ร่วมกันทั้ง dry-run และ commit (ต่างกันแค่ว่า commit=true ถึงจะเขียนลง DB จริง) -
    /// ตรงตาม wireframe nf-import (จับคู่คอลัมน์ + ตรวจ) และ nf-password (ตั้งรหัสเริ่มต้น)
    /// </summary>
    private static async Task<ImportOutcome> ProcessImportAsync(ImportStudentsRequest req, AppDbContext db, bool commit)
    {
        if (string.IsNullOrWhiteSpace(req.CsvText))
            return new ImportOutcome(null, "ไฟล์ CSV ว่างเปล่า");
        if (!Enum.IsDefined(typeof(ImportDuplicatePolicy), req.DuplicatePolicy))
            return new ImportOutcome(null, "DuplicatePolicy ไม่ถูกต้อง");
        if (!Enum.IsDefined(typeof(ImportPasswordPolicy), req.PasswordPolicy))
            return new ImportOutcome(null, "PasswordPolicy ไม่ถูกต้อง");

        var passwordPolicy = (ImportPasswordPolicy)req.PasswordPolicy;
        if (passwordPolicy == ImportPasswordPolicy.SameForAll && string.IsNullOrWhiteSpace(req.FixedPassword))
            return new ImportOutcome(null, "ต้องระบุรหัสผ่านที่ใช้ร่วมกันทั้งชุด");

        var settings = await SignInSettingsEndpoints.GetOrCreateSignInSettingsAsync(db);
        if (passwordPolicy == ImportPasswordPolicy.SameForAll && req.FixedPassword!.Length < settings.PasswordMinLength)
            return new ImportOutcome(null, $"รหัสผ่านที่ใช้ร่วมกันต้องมีอย่างน้อย {settings.PasswordMinLength} ตัว");

        var csvRows = ParseCsv(req.CsvText);
        if (csvRows.Count < 2)
            return new ImportOutcome(null, "ไฟล์ CSV ต้องมีหัวคอลัมน์ + ข้อมูลอย่างน้อย 1 แถว");

        var header = csvRows[0];
        int IndexOf(string? columnName) => string.IsNullOrWhiteSpace(columnName)
            ? -1
            : Array.FindIndex(header, h => string.Equals(h, columnName, StringComparison.OrdinalIgnoreCase));

        var codeIdx = IndexOf(req.StudentCodeColumn);
        var nameIdx = IndexOf(req.FullNameColumn);
        var emailIdx = IndexOf(req.EmailColumn);
        var classIdx = IndexOf(req.ClassNameColumn);
        var birthIdx = IndexOf(req.BirthDateColumn);

        if (codeIdx < 0) return new ImportOutcome(null, $"ไม่พบคอลัมน์ '{req.StudentCodeColumn}' (รหัสนักเรียน) ในไฟล์");
        if (nameIdx < 0) return new ImportOutcome(null, $"ไม่พบคอลัมน์ '{req.FullNameColumn}' (ชื่อ-สกุล) ในไฟล์");
        if (classIdx < 0) return new ImportOutcome(null, $"ไม่พบคอลัมน์ '{req.ClassNameColumn}' (ชั้น/ห้องเรียน) ในไฟล์");

        var duplicatePolicy = (ImportDuplicatePolicy)req.DuplicatePolicy;

        // โหลดนักเรียนที่มีอยู่แล้วทั้งหมดมาเช็คซ้ำใน memory ครั้งเดียว (จำนวนนักเรียนทั้งโรงเรียนไม่เกิน
        // หลักพัน - โหลดทีเดียวเร็วกว่า query ทีละแถวของไฟล์ที่อาจมีหลายร้อยแถว)
        var existingByCode = await db.Students.ToDictionaryAsync(s => s.StudentCode, StringComparer.Ordinal);
        var existingByEmail = await db.Students.Where(s => s.SchoolEmail != null).ToDictionaryAsync(s => s.SchoolEmail!.ToLowerInvariant());

        var codesSeenInFile = new HashSet<string>(StringComparer.Ordinal);
        var emailsSeenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rows = new List<ImportRowResult>();
        var newCount = 0;
        var dupUpdateCount = 0;
        var dupSkipCount = 0;
        var errorCount = 0;

        for (var i = 1; i < csvRows.Count; i++)
        {
            var cols = csvRows[i];
            var rowNumber = i + 1; // แถว 1 คือหัวคอลัมน์ (ตรงกับที่ nf-import นับ "แถว" บนหน้าจอ)

            string Col(int idx) => idx >= 0 && idx < cols.Length ? cols[idx].Trim() : string.Empty;

            var code = Col(codeIdx);
            var name = Col(nameIdx);
            var email = emailIdx >= 0 ? Col(emailIdx) : string.Empty;
            var className = Col(classIdx);
            var birthRaw = birthIdx >= 0 ? Col(birthIdx) : string.Empty;

            if (string.IsNullOrEmpty(email)) email = string.Empty; // normalize - เก็บเป็น "" ชั่วคราวในแถวนี้ แปลงเป็น null ตอนเขียนจริง

            string? error = null;
            if (string.IsNullOrEmpty(code)) error = "ไม่มีรหัสนักเรียน";
            else if (string.IsNullOrEmpty(name)) error = "ไม่มีชื่อ-สกุล";
            else if (string.IsNullOrEmpty(className)) error = "ไม่มีชั้น/ห้องเรียน";
            else if (!string.IsNullOrEmpty(email) && !email.Contains('@')) error = "เมลไม่ถูกต้อง";
            else if (!codesSeenInFile.Add(code)) error = "รหัสนักเรียนซ้ำกันในไฟล์เดียวกัน";
            else if (!string.IsNullOrEmpty(email) && !emailsSeenInFile.Add(email))
                error = "เมลซ้ำกันในไฟล์เดียวกัน";
            else if (!string.IsNullOrEmpty(email) && existingByEmail.TryGetValue(email.ToLowerInvariant(), out var emailOwner) && emailOwner.StudentCode != code)
                error = $"เมลซ้ำกับนักเรียน '{emailOwner.StudentCode}' ที่มีอยู่แล้ว";

            var isDuplicate = existingByCode.ContainsKey(code);
            string resultLabel;
            string? generatedPassword = null;

            if (error is not null)
            {
                resultLabel = "Error";
                errorCount++;
            }
            else if (isDuplicate)
            {
                if (duplicatePolicy == ImportDuplicatePolicy.SkipExisting)
                {
                    resultLabel = "DuplicateSkip";
                    dupSkipCount++;
                }
                else
                {
                    resultLabel = "DuplicateUpdate";
                    dupUpdateCount++;
                    if (commit)
                    {
                        var existing = existingByCode[code];
                        existing.FullName = name;
                        existing.ClassName = className;
                        if (!string.IsNullOrEmpty(email)) existing.SchoolEmail = email;
                        existing.UpdatedAt = DateTime.UtcNow;
                        // ไม่แตะรหัสผ่าน/MustChangePassword ตามที่ roadmap ระบุตรงๆ
                    }
                }
            }
            else
            {
                resultLabel = "New";
                newCount++;
                if (commit)
                {
                    DateOnly? birthDate = null;
                    if (!string.IsNullOrEmpty(birthRaw) &&
                        (DateOnly.TryParse(birthRaw, out var parsed)))
                    {
                        birthDate = parsed;
                    }

                    generatedPassword = passwordPolicy switch
                    {
                        // ตรวจแล้วว่า FixedPassword ไม่ว่างตั้งแต่ต้นฟังก์ชัน (ดูเงื่อนไข ImportPasswordPolicy.SameForAll ด้านบน)
                        ImportPasswordPolicy.SameForAll => req.FixedPassword!,
                        ImportPasswordPolicy.BirthDate => StudentPasswordGenerator.FromBirthDate(birthDate) ?? StudentPasswordGenerator.GenerateRandomSixDigit(),
                        _ => StudentPasswordGenerator.GenerateRandomSixDigit()
                    };

                    var salt = PasswordHasher.GenerateSalt();
                    var newStudent = new Student
                    {
                        StudentCode = code,
                        FullName = name,
                        SchoolEmail = string.IsNullOrEmpty(email) ? null : email,
                        ClassName = className,
                        PasswordSalt = salt,
                        PasswordHash = PasswordHasher.HashPassword(generatedPassword, salt),
                        MustChangePassword = req.ForceChangePasswordOnFirstLogin,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Students.Add(newStudent);
                    // กันไฟล์เดียวกันมีรหัสซ้ำกันสองแถว (ควรถูกจับไปแล้วที่ codesSeenInFile ข้างบน แต่กันพลาดซ้ำชั้น)
                    existingByCode[code] = newStudent;
                }
            }

            rows.Add(new ImportRowResult(rowNumber, code, name, string.IsNullOrEmpty(email) ? null : email, className, resultLabel, error, generatedPassword));
        }

        if (commit)
        {
            await db.SaveChangesAsync();
        }

        return new ImportOutcome(new ImportSummary(csvRows.Count - 1, newCount, dupUpdateCount, dupSkipCount, errorCount, rows), null);
    }

    /// <summary>
    /// CSV parser ขั้นต่ำ (RFC4180-ish) - รองรับ field ที่ครอบด้วย "..." และ "" escape ภายใน แต่ไม่รองรับ
    /// field ที่มี newline ฝังอยู่ข้างใน (ไม่พบในไฟล์ export จริงของโปรแกรมทั่วไปที่โรงเรียนใช้ เช่น Excel/
    /// Google Sheets export - ยอมรับข้อจำกัดนี้แทนที่จะพึ่ง NuGet package เพิ่มสำหรับงานเท่านี้)
    /// </summary>
    private static List<string[]> ParseCsv(string csvText)
    {
        var rows = new List<string[]>();
        using var reader = new StringReader(csvText);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0) continue;
            rows.Add(ParseCsvLine(line));
        }
        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        fields.Add(sb.ToString());
        return fields.ToArray();
    }
}
