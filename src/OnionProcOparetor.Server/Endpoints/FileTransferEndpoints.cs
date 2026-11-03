using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnionProcOparetor.Server.Data;
using OnionProcOparetor.Server.Models;
using OnionProcOparetor.Server.Services;
using static OnionProcOparetor.Server.Endpoints.AuthHelpers;

namespace OnionProcOparetor.Server.Endpoints;

// ---- Request DTOs ของแจก/เก็บไฟล์ (WP4-3, 2026-11-01) - เก็บในไฟล์นี้เองแบบเดียวกับ
// LaunchableProgramEndpoints.cs ไม่แยกไป EndpointRequests.cs เพราะใช้แค่ในกลุ่มนี้กลุ่มเดียว ----
record CreateCollectionJobRequest(string Name, string? FileNamePattern, DateTime? ClosesAt, int? RoomId);

/// <summary>
/// แจก/เก็บไฟล์ (WP4-3, 2026-11-01 - ขั้น 4 เครื่องมือสอน) - ดู wireframe docs/wireframes/tc-files.dc.html
/// และ Models/DistributedFile.cs, FileCollectionJob.cs, CollectedFile.cs, Services/FileTransferPaths.cs
///
/// ไม่มี endpoint ส่ง command "DownloadFile"/"CollectFile" ไปเครื่องเป้าหมายในไฟล์นี้ - Console เรียก
/// POST /api/commands/{clientGuid} เดิม (CommandEndpoints.cs) เองทีละเครื่อง เหมือน pattern ของ WP4-1
/// (OpenUrlWindow) ไฟล์นี้มีหน้าที่แค่ "เก็บ/แจกตัวไฟล์จริง" และ "เป็นแหล่งข้อมูลให้ Agent re-validate
/// ก่อนรันคำสั่งจริง" เท่านั้น
/// </summary>
public static class FileTransferEndpoints
{
    /// <summary>เผื่อ multipart overhead (boundary, field อื่นๆ) เหนือ cap ที่ตั้งไว้ - กันไฟล์ขนาดเท่า cap
    /// เป๊ะๆ ถูกปัดตกเพราะ multipart ทั้งก้อนใหญ่กว่าตัวไฟล์เล็กน้อย</summary>
    private const int MultipartOverheadBytes = 64 * 1024;

    public static WebApplication MapFileTransferEndpoints(this WebApplication app)
    {
        // ---- ตั้งค่า (อ่านอย่างเดียว - แก้ผ่าน appsettings.json เท่านั้น ดู Models/FileTransferOptions.cs) ----
        app.MapGet("/api/files/settings", async (HttpRequest request, AppDbContext db, IOptions<FileTransferOptions> options) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            return Results.Ok(new { maxFileSizeMb = options.Value.MaxFileSizeMb });
        });

        // ---- แจกไฟล์: ครูอัปโหลดไฟล์เดียวมาเก็บไว้ที่ Server ก่อน แล้ว Console ค่อยสั่ง command
        // "DownloadFile" ให้เครื่องเป้าหมายแต่ละเครื่องแยกอีกที (ดู doc comment บนคลาสนี้) ----
        app.MapPost("/api/files/distribute", async (HttpRequest request, AppDbContext db, IOptions<FileTransferOptions> options, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var maxBytes = (long)options.Value.MaxFileSizeMb * 1024 * 1024;

            // อนุญาต request body ใหญ่กว่า default ของ Kestrel (ตั้งไว้ 4MB ทั้ง Server ใน Program.cs เพื่อ
            // กัน endpoint อื่นที่ไม่ auth เช่น /api/logs) - เฉพาะ endpoint นี้ที่ต้องรับไฟล์ใหญ่กว่านั้นจริง
            // ต้องตั้ง "ก่อน" อ่าน body เสมอ (ReadFormAsync ด้านล่าง) ไม่งั้น Kestrel ปฏิเสธกลางทาง
            var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is not null && !sizeFeature.IsReadOnly)
            {
                sizeFeature.MaxRequestBodySize = maxBytes + MultipartOverheadBytes;
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "ต้องส่งเป็น multipart/form-data (field 'file')" });
            }

            Microsoft.AspNetCore.Http.IFormCollection form;
            try
            {
                form = await request.ReadFormAsync();
            }
            catch (Exception ex)
            {
                // เกิดได้ถ้าไฟล์เกิน MaxRequestBodySize ที่ตั้งไว้ข้างบน (Kestrel ตัดการเชื่อมต่อกลางทาง)
                return Results.Json(new { error = $"อ่านไฟล์ไม่สำเร็จ (อาจใหญ่เกิน {options.Value.MaxFileSizeMb} MB): {ex.Message}" }, statusCode: 413);
            }

            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "ไม่มีไฟล์แนบมา (field ต้องชื่อ 'file')" });
            }

            if (file.Length > maxBytes)
            {
                return Results.Json(new { error = $"ไฟล์ใหญ่เกินกำหนด ({file.Length / 1024 / 1024} MB > สูงสุด {options.Value.MaxFileSizeMb} MB)" }, statusCode: 413);
            }

            var originalFileName = FileTransferPaths.SanitizeFileName(file.FileName, "distributed-file");
            var storedFileName = Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(originalFileName);
            var fullPath = FileTransferPaths.BuildDistributedFilePath(options.Value, storedFileName);

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            await using (var outStream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(outStream);
            }

            var distributedFile = new DistributedFile
            {
                OriginalFileName = originalFileName,
                StoredFileName = storedFileName,
                SizeBytes = file.Length,
                ContentType = file.ContentType,
                UploadedAt = DateTime.UtcNow,
                UploadedByUsername = admin.Username
            };
            db.DistributedFiles.Add(distributedFile);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "FileDistributed",
                $"{admin.Username} อัปโหลดไฟล์แจก '{originalFileName}' ({file.Length} bytes) เตรียมส่งทั้งห้อง");

            return Results.Created($"/api/files/distributed/{distributedFile.Id}", new
            {
                distributedFile.Id,
                fileName = distributedFile.OriginalFileName,
                distributedFile.SizeBytes
            });
        });

        // ---- Agent ดึงไฟล์แจกจริง (ตอบรับ command "DownloadFile") - agent-auth ไม่ใช่ admin token ----
        app.MapGet("/api/files/distributed/{id:int}/content", async (
            int id, HttpRequest httpRequest, AppDbContext db, IOptions<FileTransferOptions> options, AgentAuthenticator agentAuth, IActivityLogService activityLog) =>
        {
            var (allowed, _) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "GET /api/files/distributed/{id}/content");
            if (!allowed)
                return Results.Unauthorized();

            var record = await db.DistributedFiles.FindAsync(id);
            if (record is null)
                return Results.NotFound();

            var fullPath = FileTransferPaths.BuildDistributedFilePath(options.Value, record.StoredFileName);
            if (!System.IO.File.Exists(fullPath))
            {
                // ไฟล์บนดิสก์หายไป (เช่นถูกลบมือ/ย้าย StoragePath) แต่ยังมีแถว DB อยู่ - 404 ชัดเจนดีกว่า throw
                return Results.NotFound(new { error = "ไฟล์นี้หายไปจากดิสก์ Server แล้ว (อาจถูกลบ/ย้ายที่เก็บ)" });
            }

            // ชื่อไฟล์ที่ Agent เห็นใน Content-Disposition - ใช้ OriginalFileName ที่ sanitize ไว้แล้วตอน
            // อัปโหลด (ไม่ใช่ StoredFileName ที่เป็น Guid) Agent sanitize ซ้ำอีกชั้นเองก่อนเขียนดิสก์จริง
            // (defense-in-depth - ไม่เชื่อ header นี้ 100% ฝั่ง Agent เช่นกัน)
            return Results.File(fullPath, record.ContentType ?? "application/octet-stream", record.OriginalFileName);
        });

        // ---- เก็บไฟล์: สร้างงาน ----
        app.MapPost("/api/files/collection-jobs", async (CreateCollectionJobRequest req, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();
            if (admin.HasDefaultPassword)
                return Results.Json(new { error = "Password change required before continuing." }, statusCode: 403);

            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name))
                return Results.BadRequest(new { error = "ต้องระบุชื่องาน" });

            var job = new FileCollectionJob
            {
                Name = name,
                FileNamePattern = string.IsNullOrWhiteSpace(req.FileNamePattern) ? null : req.FileNamePattern.Trim(),
                ClosesAt = req.ClosesAt,
                RoomId = req.RoomId,
                CreatedAt = DateTime.UtcNow,
                CreatedByUsername = admin.Username
            };
            db.FileCollectionJobs.Add(job);
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "FileCollectionJobCreated",
                $"{admin.Username} สร้างงานเก็บไฟล์ '{job.Name}' (ชนิดไฟล์: {job.FileNamePattern ?? "ทุกชนิด"}, ปิดรับ: {job.ClosesAt?.ToString() ?? "ไม่กำหนด"})");

            return Results.Created($"/api/files/collection-jobs/{job.Id}", job);
        });

        // ---- Agent re-validate ก่อนบอก AgentTray ให้โชว์ตัวเลือกไฟล์ - agent-auth เหมือน
        // LaunchableProgramEndpoints' /for-agent (pattern เดียวกัน) ----
        app.MapGet("/api/files/collection-jobs/{id:int}/for-agent", async (
            int id, HttpRequest httpRequest, AppDbContext db, IOptions<FileTransferOptions> options, AgentAuthenticator agentAuth, IActivityLogService activityLog) =>
        {
            var (allowed, _) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "GET /api/files/collection-jobs/{id}/for-agent");
            if (!allowed)
                return Results.Unauthorized();

            var job = await db.FileCollectionJobs.FindAsync(id);
            if (job is null)
                return Results.NotFound();

            return Results.Ok(new
            {
                job.Id,
                job.Name,
                job.FileNamePattern,
                job.ClosesAt,
                job.IsOpen,
                maxFileSizeMb = options.Value.MaxFileSizeMb
            });
        });

        // ---- ครูกด "ปิดงาน" เอง (ก่อนถึง ClosesAt หรือไม่กำหนด ClosesAt เลยก็ได้) ----
        app.MapPost("/api/files/collection-jobs/{id:int}/close", async (int id, HttpRequest request, AppDbContext db, IActivityLogService activityLog) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            var job = await db.FileCollectionJobs.FindAsync(id);
            if (job is null)
                return Results.NotFound();

            job.IsClosedManually = true;
            await db.SaveChangesAsync();

            await activityLog.LogAdminActionAsync(admin, "FileCollectionJobClosed", $"{admin.Username} ปิดงานเก็บไฟล์ '{job.Name}' เอง (ไม่รับไฟล์เพิ่ม)");

            return Results.Ok(job);
        });

        // ---- สถานะรายเครื่องของงานหนึ่ง - เป้าหมาย (ตามห้อง/ทั้งหมด) คำนวณสดทุกครั้ง ดู doc comment บน
        // FileCollectionJob.RoomId ----
        app.MapGet("/api/files/collection-jobs/{id:int}/status", async (int id, HttpRequest request, AppDbContext db, IOptions<FileTransferOptions> options) =>
        {
            var admin = await GetAuthorizedAdmin(request, db);
            if (admin is null)
                return Results.Unauthorized();

            var job = await db.FileCollectionJobs.FindAsync(id);
            if (job is null)
                return Results.NotFound();

            var targetsQuery = db.ClientMachines.AsQueryable();
            if (job.RoomId is not null)
            {
                targetsQuery = targetsQuery.Where(c => c.RoomId == job.RoomId);
            }
            var targets = await targetsQuery.OrderBy(c => c.MachineName).ToListAsync();

            var collected = await db.CollectedFiles.Where(f => f.JobId == id).ToListAsync();
            var collectedByClientGuid = collected.ToDictionary(f => f.ClientGuid, StringComparer.OrdinalIgnoreCase);

            // นักเรียนที่ login อยู่ "ตอนนี้" ของเครื่องที่ยังไม่ส่งไฟล์ - ใช้โชว์ชื่อในตารางให้ครูรู้ว่า
            // ควรไปตามใคร (ของเครื่องที่ส่งแล้วใช้ CollectedFile.StudentName ที่ frozen ไว้ตอนส่งแทน - ดู
            // doc comment บน CollectedFile.StudentName)
            var clientGuids = targets.Select(c => c.ClientGuid).ToList();
            var activeSessions = await db.StudentSessions
                .Where(s => s.EndedAt == null && clientGuids.Contains(s.ClientGuid))
                .ToListAsync();
            var studentIds = activeSessions.Select(s => s.StudentId).Distinct().ToList();
            var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
            var activeSessionByClientGuid = activeSessions.ToDictionary(s => s.ClientGuid, StringComparer.OrdinalIgnoreCase);

            var rows = targets.Select(client =>
            {
                collectedByClientGuid.TryGetValue(client.ClientGuid, out var file);

                string? currentStudentName = null;
                if (activeSessionByClientGuid.TryGetValue(client.ClientGuid, out var session) &&
                    students.TryGetValue(session.StudentId, out var student))
                {
                    currentStudentName = student.FullName;
                }

                return new
                {
                    client.ClientGuid,
                    client.MachineName,
                    studentName = file?.StudentName ?? currentStudentName,
                    hasFile = file is not null,
                    fileName = file?.OriginalFileName,
                    collectedAt = file?.CollectedAt
                };
            }).ToList();

            return Results.Ok(new
            {
                job.Id,
                job.Name,
                job.FileNamePattern,
                job.ClosesAt,
                job.IsOpen,
                totalCount = rows.Count,
                collectedCount = rows.Count(r => r.hasFile),
                folderPath = System.IO.Path.Combine(FileTransferPaths.ResolveStorageRoot(options.Value), "collected", $"{job.Id}_{FileTransferPaths.SanitizeFolderSegment(job.Name)}"),
                rows
            });
        });

        // ---- Agent อัปโหลดไฟล์ที่นักเรียนเลือกส่ง (ส่งต่อมาจาก AgentTray ผ่าน Agent loopback) - agent-auth ----
        app.MapPost("/api/files/collection-jobs/{id:int}/upload", async (
            int id, HttpRequest httpRequest, AppDbContext db, IOptions<FileTransferOptions> options, AgentAuthenticator agentAuth, IActivityLogService activityLog) =>
        {
            var (allowed, client) = await agentAuth.AuthorizeByHeadersAsync(httpRequest, db, activityLog, "POST /api/files/collection-jobs/{id}/upload");
            if (!allowed || client is null)
                return Results.Unauthorized();

            var job = await db.FileCollectionJobs.FindAsync(id);
            if (job is null)
                return Results.NotFound(new { error = "ไม่พบงานเก็บไฟล์นี้ (อาจถูกลบไปแล้ว)" });

            if (!job.IsOpen)
            {
                return Results.Json(new { error = $"งานเก็บไฟล์ '{job.Name}' ปิดรับไฟล์ไปแล้ว" }, statusCode: 409);
            }

            var maxBytes = (long)options.Value.MaxFileSizeMb * 1024 * 1024;
            var sizeFeature = httpRequest.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is not null && !sizeFeature.IsReadOnly)
            {
                sizeFeature.MaxRequestBodySize = maxBytes + MultipartOverheadBytes;
            }

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new { error = "ต้องส่งเป็น multipart/form-data (field 'file')" });
            }

            Microsoft.AspNetCore.Http.IFormCollection form;
            try
            {
                form = await httpRequest.ReadFormAsync();
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = $"อ่านไฟล์ไม่สำเร็จ (อาจใหญ่เกิน {options.Value.MaxFileSizeMb} MB): {ex.Message}" }, statusCode: 413);
            }

            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "ไม่มีไฟล์แนบมา (field ต้องชื่อ 'file')" });
            }

            if (file.Length > maxBytes)
            {
                return Results.Json(new { error = $"ไฟล์ใหญ่เกินกำหนด ({file.Length / 1024 / 1024} MB > สูงสุด {options.Value.MaxFileSizeMb} MB)" }, statusCode: 413);
            }

            // ชื่อนักเรียนจากการล็อกอิน "ตอนนี้" ของเครื่องนี้ (ดู doc comment บน CollectedFile.StudentName) -
            // fallback ชื่อเครื่องถ้าไม่มีใคร login อยู่ (ตรงตาม docs/roadmap.md "ถ้าไม่ได้ล็อกอินใช้ชื่อเครื่อง")
            var activeSession = await db.StudentSessions
                .Where(s => s.EndedAt == null && s.ClientGuid == client.ClientGuid)
                .FirstOrDefaultAsync();
            Student? student = activeSession is null ? null : await db.Students.FindAsync(activeSession.StudentId);

            var studentName = student?.FullName;
            var collectorFolderName = FileTransferPaths.SanitizeFolderSegment(studentName ?? client.MachineName, client.MachineName);
            var sanitizedJobName = FileTransferPaths.SanitizeFolderSegment(job.Name);
            var sanitizedFileName = FileTransferPaths.SanitizeFileName(file.FileName, "submitted-file");

            var fullPath = FileTransferPaths.BuildCollectedFilePath(options.Value, job.Id, sanitizedJobName, collectorFolderName, sanitizedFileName);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            await using (var outStream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(outStream);
            }

            var relativePath = FileTransferPaths.BuildCollectedFileRelativePath(job.Id, sanitizedJobName, collectorFolderName, sanitizedFileName);

            // upsert ตาม (JobId, ClientGuid) - ส่งซ้ำทับของเดิม (ดู doc comment บน CollectedFile)
            var existing = await db.CollectedFiles.FirstOrDefaultAsync(f => f.JobId == job.Id && f.ClientGuid == client.ClientGuid);
            if (existing is not null)
            {
                existing.StudentName = studentName;
                existing.MachineName = client.MachineName;
                existing.OriginalFileName = sanitizedFileName;
                existing.StoredRelativePath = relativePath;
                existing.SizeBytes = file.Length;
                existing.CollectedAt = DateTime.UtcNow;
            }
            else
            {
                db.CollectedFiles.Add(new CollectedFile
                {
                    JobId = job.Id,
                    ClientGuid = client.ClientGuid,
                    MachineName = client.MachineName,
                    StudentName = studentName,
                    OriginalFileName = sanitizedFileName,
                    StoredRelativePath = relativePath,
                    SizeBytes = file.Length,
                    CollectedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();

            await activityLog.LogStudentActionAsync(student, "FileCollected",
                $"{(student?.FullName ?? client.MachineName)} ส่งไฟล์ '{sanitizedFileName}' สำหรับงาน '{job.Name}'",
                client.ClientGuid, client.MachineName);

            return Results.Ok(new { success = true, fileName = sanitizedFileName });
        });

        return app;
    }
}
