# Onion ProcOperator — ชุดไอคอน + icon database

ไอคอนหัวหอม 2 โทน (amber `#F5A623` / cream `#FFE3AD`) ถอดเป็น SVG จากภาพที่วาดเอง
มี 3 เวอร์ชันความละเอียด และไอคอนสถานะ 3 แบบสำหรับ tray

## โครงสร้าง

```
icons.db                 ฐานข้อมูล SQLite: SVG ทุกเวอร์ชัน + PNG หลายขนาด + .ico ของทุกไอคอน
schema.sql               โครงสร้างตาราง (icon / icon_svg / icon_raster)
icon-src/<ชื่อ>/         ต้นฉบับของแต่ละไอคอน (meta.json + full.svg [+ small.svg, tiny.svg])
export/<ชื่อ>/          ไฟล์พร้อมใช้ที่ build ออกมา (.ico, .svg, .png หลายขนาด)
build_icon_db.py         icon-src/ -> icons.db + export/
make_pack.py             source/onion-source.png -> SVG 3 เวอร์ชัน -> icon-src/ (แอปหลัก + tray)
make_roster.py           ไอคอน roster manager (หัวหอมสีฟ้า รากเป็นกลุ่มผู้ใช้) -> icon-src/roster-app/
csharp/IconStore.cs      ตัวอย่างโหลดไอคอนจาก icons.db ใน WPF (ยังไม่ได้ compile)
preview.png              ภาพรวมทุกไอคอนที่ 16/24/32/48/256px บนพื้นสว่างและมืด
```

ไอคอนที่มีตอนนี้ (`icon.key`): `app`, `tray.connected`, `tray.disconnected`, `tray.error`, `roster.app`

`roster.app` คือไอคอนของ roster manager: หัวหอมโทนฟ้า (`#2A6FD6` / `#D9E8FF`) ที่รากถูกแทนด้วยกลุ่มผู้ใช้ 3 คน
เวอร์ชัน `tiny` (16px) เหลือผู้ใช้คนเดียว เพราะกลุ่มคนจะเป็นรอยเลือนที่ขนาดนั้น

## 3 เวอร์ชันความละเอียด

| เวอร์ชัน | ใช้ที่ขนาด | ต่างจาก full อย่างไร |
|---|---|---|
| `full` | 48px ขึ้นไป | ถอดตรงจากภาพต้นฉบับ |
| `small` | 17–32px | เส้นแบ่งหนาขึ้น ตัดรายละเอียดเล็กออก |
| `tiny` | 16px | เหลือเฉพาะรูปทรง + แถบเฉียง |

ไฟล์ `.ico` และ PNG เลือกเวอร์ชันตามขนาดให้อัตโนมัติ

## ใช้เป็นไอคอนโปรแกรม

```xml
<!-- .csproj ของ Agent / AgentTray / Console -->
<PropertyGroup>
  <ApplicationIcon>Assets\app.ico</ApplicationIcon>
</PropertyGroup>
```
```xml
<!-- WiX v5: ไอคอนใน Add/Remove Programs -->
<Icon Id="AppIcon" SourceFile="Assets\app.ico" />
<Property Id="ARPPRODUCTICON" Value="AppIcon" />
```
ไฟล์อยู่ที่ `export/app/app.ico`

## ใช้ icon database

ติดตั้ง `icons.db` ไว้ข้าง exe แล้วเปิดแบบ read-only (`csharp/IconStore.cs`):

```csharp
using var icons = new IconStore(Path.Combine(AppContext.BaseDirectory, "icons.db"));
image.Source = icons.GetImage("tray.connected", 24);      // WPF Image
var list = icons.List(category: "status");               // ทำ icon picker
```

AgentTray (NotifyIcon):
```csharp
var bytes = icons.GetIcoBytes("tray.error")!;
notifyIcon.Icon = new System.Drawing.Icon(new MemoryStream(bytes), new System.Drawing.Size(16, 16));
```

ดูจาก SQL ตรงๆ:
```sql
SELECT key, name_th, category FROM icon;
SELECT size, length(png) FROM icon_raster WHERE icon_id = (SELECT id FROM icon WHERE key = 'app');
```

**เชื่อมกับ roster manager:** เพิ่มคอลัมน์ `IconKey` (text, nullable) ใน entity ของ roster แล้วเก็บค่า `icon.key`
ไม่ต้องทำ foreign key ข้ามไฟล์ DB — ให้ UI ค้นด้วย key ตอนแสดงผล และใช้ `sha256` เป็น cache key

## เพิ่มไอคอนใหม่

1. สร้างโฟลเดอร์ `icon-src/<ชื่อ>/` ใส่ `full.svg` (viewBox สี่เหลี่ยมจัตุรัส) และ `meta.json`
   ```json
   { "key": "roster.locked", "name": "Locked", "name_th": "ล็อกหน้าจอ", "category": "status" }
   ```
2. (ไม่บังคับ) ใส่ `small.svg` / `tiny.svg` ถ้า full ดูไม่ออกที่ขนาดเล็ก
3. รัน `python3 build_icon_db.py` — `icons.db` ถูกสร้างใหม่ทั้งไฟล์ อย่าแก้ด้วยมือ

ต้องมี: `pip install cairosvg pillow` (และ `potracer opencv-python-headless numpy` ถ้าจะรัน `make_pack.py` ใหม่)

## ข้อจำกัด

- `IconStore.cs` ยังไม่เคย compile หรือรันกับโปรเจกต์จริง
- ไอคอนสถานะมีแค่ connected / disconnected / error — สถานะอื่นของ roster (เช่น locked, missing) ยังไม่ได้ทำ
- กลุ่มผู้ใช้ใน `roster.app` อ่านชัดตั้งแต่ราว 48px; ที่ 24–32px เห็นเป็นกลุ่มก้อนเล็กๆ ที่โคนหอม
- ที่ 16px ป้ายสถานะเหลือเป็นจุดสี ไม่มีสัญลักษณ์ข้างใน (สี เขียว/เทา/แดง แยกด้วยสีอย่างเดียว)
