using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnionProcOparetor.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAndDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AdminUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "AdminUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // WP4 (2026-10-30): backfill DisplayName ของบัญชีเดิมทุกบัญชีให้เท่ากับ Username ตาม roadmap
            // ("DisplayName เริ่มต้น = Username") - คอลัมน์ข้างบนเพิ่งถูกเติมด้วยค่า default "" ว่างเปล่า
            // ทุกแถว ต้องมี UPDATE ตรงนี้ด้วยไม่งั้นบัญชีเดิมทุกบัญชีจะมีชื่อที่แสดงว่างเปล่าตลอดไป
            // Role ไม่ต้องมี UPDATE เพิ่ม เพราะ default 0 (Admin) ตรงกับที่ต้องการอยู่แล้ว
            // (ไม่ให้ใครโดนล็อกออกจากระบบตอน deploy ครั้งแรก)
            migrationBuilder.Sql("UPDATE AdminUsers SET DisplayName = Username WHERE DisplayName = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "AdminUsers");
        }
    }
}
