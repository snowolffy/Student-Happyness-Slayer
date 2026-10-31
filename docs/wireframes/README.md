# Wireframes (export จาก canvas ของผู้ใช้ · 31 ต.ค. 2569)

โฟลเดอร์นี้คือต้นฉบับ HTML ของ wireframe ทุกหน้า สำเนามาจาก canvas ส่วนตัวของผู้ใช้ เพื่อให้ worker เปิดอ่านได้
ไฟล์เปิดในเบราว์เซอร์ตรงๆ ไม่ได้ เพราะต้องใช้ runtime ของ canvas **ให้อ่านเป็นโค้ด** จะเห็นโครงหน้า ข้อความบนปุ่ม คอลัมน์ และหมายเหตุ

## วิธีอ่าน
- **สีเทา = ตัวแทนเท่านั้น** ของจริงใช้ theme token เดิมของแต่ละแอป (Console: `Console/Theme/Colors.*.xaml`)
- `outline` เส้นประสีส้ม (`#b45309`) และป้าย "เสนอแก้" = ของเดิมที่ต้องปรับ
- `outline` เส้นประสีน้ำเงิน (`#1d4ed8`) และป้าย "ใหม่" = ฟีเจอร์ใหม่
- ข้อมูลตัวอย่างอยู่ใน `renderVals()` ท้ายไฟล์ ข้อความในวงเล็บเหลี่ยม เช่น `[ชื่อ]` เป็น placeholder
- ถ้า wireframe ขัดกับ `docs/roadmap.md` **ให้ยึด roadmap** และระบุจุดที่ขัดไว้ในรายงาน
- `decisions.dc.html` = สรุปการตัดสินใจทั้งหมด (ข้อความเดียวกับใน roadmap)

## สารบัญตามแอปและขั้น

| กลุ่ม | ไฟล์ | ขั้น |
|---|---|---|
| ภาพรวม | `Main` (แผนผัง), `decisions` | – |
| Console | `con-login`, `con-clients`, `con-clients-dark`, `con-users`, `con-settings`, `con-logs` | 1 (ทำแล้ว) |
| Console | `con-actionpanel`, `con-dialogs`, `con-liveview`, `con-rules`, `con-programreview` | มีอยู่แล้ว / เสนอแก้ |
| Console | `con-seatmap` | 3–4 (ผังที่นั่ง) |
| Website (Console) | `con-website`, `con-website-allow`, `con-website-history`, `nf-exam` | 2 |
| Sign-in | `nf-signin-settings`, `nf-roster-login`, `nf-roster`, `nf-import`, `nf-password`, `nf-print`, `nf-promote`, `nf-settings-more` | 3 |
| ห้อง / ประวัติ | `nf-rooms`, `nf-attendance` (= ประวัติการใช้เครื่อง) | 1 (ห้อง) / 3 (ประวัติ) |
| จอนักเรียน | `st-login`, `st-login-states`, `st-session`, `st-staff-start`, `st-emergency`, `st-locked`, `st-notices`, `st-tray` | 3 (ส่วนใหญ่) |
| OnlineConsole (มือถือ 390px) | `oc-*` (`oc-exam` = ขั้น 2, `oc-emergency` = ขั้น 3) | ตามฟีเจอร์ |
| เครื่องมือสอน | `tc-openurl`, `tc-raisehand`, `tc-files`, `tc-broadcast`, `tc-broadcast-student` | 4 |
| รายงาน / ดูแลเครื่อง | `rp-student`, `rp-power`, `rp-inventory` | 5 |
| เครื่องมือเสริม | `tl-remotebridge`, `tl-uninstall`, `tl-installer` | – |

## การแก้ที่ผู้ใช้ทำเองบน canvas (ต้องยึดตามนี้)
- `st-locked`: **พื้นหลังจอล็อกเป็นสีดำ** (`#000000`) แทนสีกรมท่าที่เสนอไว้
