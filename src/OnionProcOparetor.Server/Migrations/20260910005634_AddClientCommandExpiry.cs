using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddClientCommandExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ClientCommands",
                type: "TEXT",
                nullable: true);

            // ==================== ปลดชนวนคิวที่ค้างอยู่ในฐานข้อมูลจริง ====================
            // เพิ่มคอลัมน์เฉยๆ ไม่พอ: แถวเก่าทุกแถวจะได้ ExpiresAt = NULL ซึ่งกติกาใหม่แปลว่า
            // "ไม่มีวันหมดอายุ" = คำสั่ง Shutdown ที่ค้างรอเครื่องซึ่งปิดอยู่ **ก็ยังจะยิงอยู่ดี**
            // ตอนเครื่องนั้นถูกเปิด ซึ่งคือบั๊กที่กำลังจะแก้พอดี
            //
            // คำสั่งประเภท "เหตุการณ์" ที่ค้างอยู่ ณ ตอน migrate ถือว่าหมดอายุทั้งหมดโดยนิยาม -
            // ไม่มีทางที่มันจะสร้างขึ้นภายใน 3 นาทีที่ผ่านมาแล้วรอด downtime ของการอัปเดต Server
            // ส่วนคำสั่งประเภท "สถานะ" (UpdateSettings/SetTailscaleLockdown/RefreshRules) ปล่อยเป็น
            // NULL ไว้เหมือนเดิม เพราะส่งถึงช้าก็ยังถูกต้องและควรถึง
            //
            // รายชื่อนี้ต้องตรงกับ CommandExpiry.DurableCommandTypes - เขียนซ้ำที่นี่โดยเจตนา:
            // migration ต้องอธิบายสิ่งที่มันทำกับข้อมูลได้ด้วยตัวเอง และต้องไม่เปลี่ยนความหมาย
            // ย้อนหลังถ้าโค้ดฝั่ง C# ถูกแก้ทีหลัง
            const string durableTypes = "('UpdateSettings','SetTailscaleLockdown','RefreshRules')";

            migrationBuilder.Sql($@"
                UPDATE ClientCommands
                SET ExpiresAt = CreatedAt
                WHERE CommandType NOT IN {durableTypes};");

            // ปิดสถานะแถวที่ยังค้างส่งให้เรียบร้อยไปเลย ไม่ต้องรอให้เครื่องนั้น poll เข้ามาก่อน -
            // เครื่องที่ปิดอยู่จะไม่ poll มาอีกเป็นสัปดาห์ก็ได้ และระหว่างนั้นครูจะเห็นคำสั่งค้างใน
            // หน้า PENDING COMMANDS โดยไม่รู้ว่ามันตายไปแล้ว
            // Status: 0=Pending 1=Delivered 2=Failed 3=Expired
            // ExecutionStatus: 0=Pending 1=Succeeded 2=Failed 3=TimedOut 4=Expired
            migrationBuilder.Sql($@"
                UPDATE ClientCommands
                SET Status = 3,
                    ExecutionStatus = CASE WHEN ExecutionStatus = 0 THEN 4 ELSE ExecutionStatus END,
                    ExecutionMessage = CASE
                        WHEN ExecutionStatus = 0
                        THEN 'หมดอายุตอนอัปเดต Server (2026-09-10) - คำสั่งค้างจากก่อนหน้านี้จะไม่ถูกส่งไปรันย้อนหลังอีก'
                        ELSE ExecutionMessage END
                WHERE Status = 0
                  AND CommandType NOT IN {durableTypes};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ClientCommands");
        }
    }
}
