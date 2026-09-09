# WPF Console — บันทึกการออกแบบ UI (2026-09-09)

บันทึกไว้เพื่อกันไม่ให้ของที่ตั้งใจทำถูก "แก้กลับ" ในอนาคต เอกสารนี้ครอบคลุมเฉพาะ
`src/OnionProcOparetor.Console` (WPF) คู่กับ [`onlineconsole-ui-notes.md`](onlineconsole-ui-notes.md)
ที่ครอบคลุมฝั่ง Blazor

เป้าหมายของงานรอบนี้: ให้ Console กับ OnlineConsole **ดูเป็นผลิตภัณฑ์เดียวกัน** ไม่ใช่สองแอปที่บังเอิญ
ต่อเซิร์ฟเวอร์ตัวเดียวกัน โดยไม่ตัดความสามารถของฝั่ง WPF ลงเพื่อให้เท่ากับเว็บ

---

## 1. กับดักที่ต้องรู้ก่อนแก้ไฟล์ธีม

### 1.1 ห้ามใช้ `{StaticResource}` กับ `Brush.*` / `Color.*` เด็ดขาด

`StaticResource` resolve **ครั้งเดียวตอน parse XAML** ใครใส่ไว้ที่ไหน ธีมตรงนั้นจะค้างอยู่กับธีมแรก
ที่โหลดตลอดไป และจะไม่มีใครสังเกตจนกว่าจะมีคนสลับไปโหมดมืดแล้วเจอกล่องขาวโพลนกล่องเดียวกลางจอ

> **ทุก token สีต้องอ้างด้วย `{DynamicResource}` เสมอ** — ส่วน `Style`/`FontFamily` ใช้
> `StaticResource` ได้ตามปกติ เพราะไม่ได้เปลี่ยนตามธีม

ฝั่ง code-behind ก็เหมือนกัน: `FindResource("Brush.X")` คืน "ค่า ณ ตอนนั้น" ครั้งเดียว ต้องใช้
`SetResourceReference(...)` แทน ซึ่งผูกเป็น DynamicResource จริง

### 1.2 Freezable ที่ประกาศ inline ใน `Style` setter ผูก DynamicResource ไม่ได้

เดิม `ConsoleGridRowStyle` เขียนสี hover เป็น
`<SolidColorBrush Color="{StaticResource Color.Accent.Light}" Opacity="0.15"/>` แบบ inline อยู่ใน
`Setter.Value` — WPF จะ **freeze** brush ตัวนั้น ทำให้ผูก DynamicResource เข้าไปไม่ได้ ธีมจะค้าง
ตอนนี้ย้ายไปเป็น token `Brush.Row.Hover` แล้ว **อย่าเขียน brush inline ใน style setter อีก**

### 1.3 ไฟล์ palette สองไฟล์ต้องมี key ครบเท่ากันเสมอ

`Theme/Colors.Light.xaml` กับ `Theme/Colors.Dark.xaml` ประกาศ `x:Key` ชุดเดียวกันเป๊ะ (ตอนนี้ 70 ตัว)
`ThemeService` สลับทั้ง dictionary ทีเดียว key ที่มีอยู่ไฟล์เดียวจะกลายเป็น DynamicResource ที่หา
ไม่เจอในอีกธีมหนึ่ง ซึ่ง **ไม่ throw** — มันเงียบแล้ววาดเป็นค่าว่าง

`ThemeService.VerifyKeyParity()` เช็คตอน startup ทุกครั้งและเขียนลง `console.log` ถ้าไม่ตรง
**เพิ่ม token เมื่อไหร่ ต้องเพิ่มสองที่**

### 1.4 คอมเมนต์ XML ห้ามมี `--`

เจอตอน build พัง 2 ครั้งในงานรอบนี้: `<!-- --bg -->` (อ้างชื่อ CSS variable) และเส้นคั่น
`<!-- ---------- -->` ผิด XML ทั้งคู่ ในไฟล์ธีมจึงเขียนชื่อ CSS variable เป็น `css:bg` แทน และใช้
`=` เป็นเส้นคั่น

### 1.5 `ThemeMode` ยังจำเป็น แม้จะ template เองเกือบหมดแล้ว

`Application.ThemeMode` / `Window.ThemeMode` (ยัง `[Experimental]` — ต้อง `#pragma warning disable WPF0001`)
คุมคอนโทรลที่เรา **ไม่ได้** re-template เอง: ComboBox, CheckBox, RadioButton, ScrollBar, DataGrid chrome
ถ้าไม่สลับตัวนี้ด้วยจะได้โหมดมืดครึ่งใบ

ตั้ง **ก่อน** วาง palette ของเราเสมอ เพราะการ set ThemeMode ทำให้ WPF ไปยุ่งกับ
`Application.Resources.MergedDictionaries` เอง (แทรก Fluent dictionary เข้ามา) — `ThemeService`
จึงหา palette ด้วย key `Theme.Name` ไม่ใช่ด้วย index ที่เลื่อนได้

---

## 2. แถบหัวหน้าต่างวาดเอง — และเหตุผลที่ไม่ใช่เรื่องความสวย

**บน Windows 10 build 19045 แถบหัวหน้าต่างของ Windows ไม่ยอมเป็นสีเข้ม**
`DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute 20) ถูกรับค่าเรียบร้อย — `DwmGetWindowAttribute` อ่านกลับมา
ได้ `1`, `hr = 0` — แต่ยังวาดขาวอยู่ดี (ของนี้เริ่มทำงานจริงบน Windows 11) ทดสอบซ้ำแล้วรวมถึงสั่ง
`SetWindowPos(SWP_FRAMECHANGED)` บังคับวาดกรอบใหม่ ก็ยังขาว

ผลคือถ้าไม่วาดแถบหัวเอง โหมดมืดจะมีแถบขาวพาดหัวทุกหน้าต่าง ซึ่งดูเหมือนของเสีย

`AppDialogWindow` (ใน `Theme/Controls.xaml`) จึงวาดแถบหัวเอง โดย:

- ใช้ **`WindowChrome`** ไม่ใช่ `WindowStyle="None"` — `WindowChrome` ยกเฉพาะ *การวาด* มาให้เรา แต่
  ยังคง *พฤติกรรม* ของ Windows ไว้ครบ (snap ที่ขอบจอ, ปุ่มบนแถบงาน, เงา, resize ด้วยขอบ)
- `ThemeService.ApplyToWindow` **ยังเรียก DWM อยู่** เผื่อเครื่อง Windows 11 และเผื่อขอบที่เราไม่ได้วาด
- ปุ่มย่อ/ขยายโผล่ตาม `ResizeMode` ของแต่ละหน้าต่าง
- ตอน `Maximized` ต้อง `Padding="7"` — เป็นท่าแก้มาตรฐานของ WindowChrome ที่หน้าต่างจะขยายเลยขอบจอ
- ปุ่มปิด/ย่อ/ขยายใช้ **attached property** (`DialogChrome.IsCloseButton` ฯลฯ) ไม่ใช่ `Click` handler
  เพราะปุ่มอยู่ใน template ที่ใช้ร่วมกัน ไม่มี code-behind ตัวไหนเป็นเจ้าของ และ
  `SystemCommands.CloseWindowCommand` ก็ไม่มี `CommandBinding` ให้เมื่อหน้าต่างใช้ template ของตัวเอง
- ต้องมี `<AdornerDecorator>` ครอบ `ContentPresenter` — ปกติ template มาตรฐานของ WPF ใส่ให้ ถ้าไม่มี
  ToolTip / validation adorner จะไม่ปรากฏเลย

---

## 3. Sidebar คือ `TabControl` ตัวเดิม

หน้า Dashboard **ไม่ได้** ถูกเขียนใหม่เป็น navigation แบบ ViewModel — มันยังเป็น `TabControl` ตัวเดิม
เปลี่ยนแค่ `ControlTemplate` (ย้าย panel ที่ `IsItemsHost` ไปคอลัมน์ซ้าย) กับ `ItemContainerStyle`
(แถวเมนูแทนแท็บแนวนอน) ผลคือ `MainTabControl_SelectionChanged`, `ScreenViewTabItem`,
`AuditLogTabItem`, `LiveViewTabItem` และโค้ดที่สั่งสลับแท็บเองยังทำงานเหมือนเดิมทั้งหมด

สามอย่างที่ต้องรู้:

- **`Nav.Group`** — `TabControl` มี items host ได้ panel เดียว จะแทรกหัวข้อกลุ่มคั่นระหว่าง item ไม่ได้
  จึงให้ *แถวแรกของกลุ่ม* วาดหัวข้อของตัวเอง แล้วซ่อนเมื่อค่าว่าง
- **`Nav.IsCollapsed`** ประกาศเป็น `Inherits` — ตั้งที่ `TabControl` ตัวเดียวแล้วไหลลงไปถึงทุกแถวเมนู
  หัวข้อกลุ่ม และปุ่มท้าย sidebar พร้อมกัน โดยไม่ต้องไล่หา element ข้าม namescope
- **`Nav.TopBar` / `Nav.StatusBar`** — แถบบนกับแถบสถานะถูก *ส่งเข้า* template ผ่าน attached property
  ไม่ได้เขียนไว้ใน template ตรง ๆ เพราะ **element ที่ประกาศใน `ControlTemplate` ไม่มี field จาก
  `x:Name` ให้ code-behind เรียกถึง** และ `DashboardWindow.xaml.cs` แตะ `StatusText` อย่างเดียว 22 จุด
  วิธีนี้ทำให้รื้อโครงหน้าจอทั้งหมดได้โดยไม่แตะ code-behind สักบรรทัด

ลำดับแท็บถูกจัดใหม่ให้กลุ่มติดกัน (Clients, Screen View, Live View, Logs | Rules | Audit Log, Users)
ปลอดภัยเพราะ **ไม่มีโค้ดตรงไหนอ้างแท็บด้วย index** — ตรวจก่อนย้ายแล้ว มีแต่ `SelectedItem` กับชื่อ TabItem

---

## 4. ไอคอน — ไม่มี emoji ไม่มี icon font

`Theme/Icons.xaml` เป็น port ของ `Components/Icon.razor` แบบตรงตัว (Geometry บน canvas 24×24 เท่ากับ
`viewBox="0 0 24 24"`) **สองไฟล์นี้ต้องมีชุดเดียวกันเสมอ** เพิ่มที่ไหนต้องเพิ่มอีกที่ด้วย — ตอนนี้ 34 ตัว
(`grid` / `minus` / `square` เพิ่มมาจากฝั่ง WPF และใส่กลับเข้า `Icon.razor` แล้วแม้เว็บยังไม่ได้ใช้)

- **`IconPresenter` ชดเชยความหนาเส้น**: `StrokeThickness = 2.0 × 24 / IconSize` (1.75 ที่ ≥28px)
  ถ้าไม่ชดเชย ไอคอน 16px จะได้เส้น 1.33px (บางจนซีด) และ 32px ได้ 2.67px (หนาเหมือนปากกาเมจิก)
  → **ความหนาเส้นจึงไม่เท่าฝั่งเว็บโดยเจตนา** (เว็บปล่อยให้ scale ตาม viewBox) ส่วน geometry เหมือนกันเป๊ะ
- **จุดที่เป็น degenerate line ใน SVG** (`x2="3.01"`) WPF วาดออกมาเป็น *ไม่มีอะไรเลย* แม้ตั้ง round cap
  จึงถูกแทนด้วย `EllipseGeometry` รัศมี **0.1** — เล็กเกือบศูนย์แต่ไม่ใช่ศูนย์ เพื่อให้พอถูกตีเส้นหนา `t`
  จะได้จุดทึบขนาด ~`t` เท่ากับที่ round cap ของ SVG ทำ (รัศมี 1 จะกลายเป็นวงแหวนกลวงตอนไอคอนใหญ่)
- **ไม่มี emoji ในโค้ดฐานนี้มาตั้งแต่แรก** สิ่งที่มีคืออักขระเรขาคณิตที่ทำหน้าที่เหมือนไอคอน
  (`ACTIONS ▾`, `◀ PREV`, `NEXT ▶`, `✕`, `←`) ซึ่งถูกแทนด้วยไอคอนเวกเตอร์หมดแล้ว — ปัญหาเดียวกับ emoji
  คือรูปร่างขึ้นกับ font ที่ระบบ fallback มาให้ ไม่ใช่ของเรา

---

## 5. หลักการที่ยึด (และที่จงใจไม่ยึดตามเว็บ)

- **badge เป็นเม็ดสีอ่อน ไม่ใช่ป้ายทึบ** — สีสถานะทับพื้นการ์ดด้วย alpha 14–20% + ตัวหนังสือสีสถานะ
  + จุดนำหน้า เก็บเป็น *alpha* ไม่ใช่สีทึบที่ผสมไว้แล้ว เพื่อให้ตามสีการ์ดไปเองถ้าวันหลังปรับ
  (เหตุผลเดียวกับที่ฝั่งเว็บ `color-mix` กับ `var(css:card)` ไม่ใช่กับสีขาว)
- **`Danger.Fill` ≠ `Danger.Default`** — ในธีมมืด `#F87171` เป็นสีแดงที่เลือกมาให้ *อ่านออกบนพื้นเข้ม*
  จึงสว่างเกินกว่าจะเป็นพื้นหลังของตัวหนังสือขาว (ขาวบน `#F87171` ≈ 2.5:1 ตกเกณฑ์) ปุ่มอันตรายจึงถม
  ด้วย `#DC2626` (≈ 4.8:1) **นี่เป็นจุดที่จงใจต่างจากเว็บ** ซึ่ง `.btn-danger` ยังใช้สีเดียวกันทั้งคู่
- **การ์ดตัวเลขคิดจากข้อมูลทั้งหมดเสมอ ไม่ใช่จากผลที่กรองแล้ว** — ไม่งั้นกด "ออนไลน์" แล้วการ์ด
  "ออฟไลน์" กลายเป็น 0 ซึ่งอ่านเหมือนเครื่องหายไปจากระบบจริง ๆ
- **แผงรายละเอียดขวาโผล่เมื่อเลือกเครื่องเดียวเท่านั้น** — เลือกหลายเครื่องคือโหมดสั่งงานหมู่ ซึ่งมี
  แถบเลือกด้านบนรับหน้าที่อยู่แล้ว
- **ยืนยันด้วยการพิมพ์ชื่อ ใช้เฉพาะปุ่มลบ** (ลบกฎ / ลบผู้ใช้) ไม่ใช้กับปิดเครื่องทั้งห้อง — ปิดเครื่อง
  กวนแต่ทำใหม่ได้ ส่วนของที่ลบไปแล้วเอาคืนไม่ได้ แรงเสียดทานที่ใส่ไปทุกที่จะกลายเป็นพิธีกรรมที่คนกดผ่าน
  โดยไม่อ่าน ซึ่งแย่กว่าไม่มี
- **ไม่ทำ responsive / breakpoint** — นั่นเป็นงานของ OnlineConsole ฝั่ง WPF มีแค่ sidebar ที่ย่อได้
- **ไม่ตัดฟีเจอร์ลงเพื่อให้เท่าเว็บ** — WPF Console มี Live View (กำแพงจอแบบ CCTV), remote control
  เต็มจอ และ block student input ซึ่ง OnlineConsole ไม่มี (12-tile grid ถูกถอดออกเพราะแบนด์วิดท์
  2026-09-04) เอามาแค่ "ภาษาดีไซน์" ไม่ใช่ "ขอบเขตฟีเจอร์"

---

## 6. เครื่องมือตรวจงาน UI (มีเฉพาะ build แบบ Debug)

`installer/publish-all.ps1` publish ด้วย `-c Release` เสมอ สวิตช์พวกนี้จึงไม่มีอยู่จริงในของที่ส่งขึ้น
เครื่องครู — ทั้งหมดอยู่ใน `#if DEBUG`

```
OnionProcOparetor.Console.exe --ui-preview                       เปิด Dashboard พร้อมข้อมูลปลอม 5 เครื่อง
                             --tab="Audit Log"                   เลือกแท็บก่อนถ่าย
                             --dialog=confirm-type               เปิดหน้าต่างย่อยแทน (17 บาน รวมหน้า login)
                             --shot=C:\path\out.png              เรนเดอร์เป็น PNG แล้วปิดโปรแกรม
```

มีไว้เพราะ **เครื่อง dev เครื่องนี้รัน Server ตัวจริงอยู่ด้วย** การจะดู Dashboard สักทีไม่ควรต้อง
login เข้าเซิร์ฟเวอร์ของจริง — โหมดนี้ใช้ `ApiClient` ที่ยังไม่ได้ login ทุก API call จึงล้มเหลวหมด
โดยเจตนา เห็นแต่โครง UI

**`--shot` เรนเดอร์ในแอปด้วย `RenderTargetBitmap` ไม่ใช่จับภาพหน้าจอ** และนี่ไม่ใช่เรื่องรสนิยม:

| วิธี | ปัญหาที่เจอจริงระหว่างทำงานนี้ |
|---|---|
| `CopyFromScreen` | จับภาพ **เบราว์เซอร์ที่บังอยู่ข้างหน้า** มาแทนหน้าต่างแอป |
| `PrintWindow(PW_RENDERFULLCONTENT)` | คืน **เฟรมขาวเปล่า** เมื่อหน้าต่างถูกบัง |
| `SetForegroundWindow` | Windows ไม่ยอมให้ process เบื้องหลังแย่ง foreground |

`RenderTargetBitmap` วาดจาก visual tree ตรง ๆ จึงได้ภาพถูกต้องเสมอไม่ว่าหน้าต่างจะถูกบัง ย่อ หรือ
มีอะไรทับอยู่ **อย่าเปลี่ยนกลับไปจับภาพหน้าจอ**

หมายเหตุสำหรับสคริปต์: `Start-Process -ArgumentList` เอา array มาต่อด้วยช่องว่างโดย **ไม่ใส่เครื่องหมาย
คำพูดให้** — `--tab=Audit Log` จะกลายเป็นสองอาร์กิวเมนต์ ต้อง escape เอง

---

## 7. สิ่งที่ยังไม่ได้ทำ / ข้อจำกัดที่รู้อยู่

- **หน้าต่างราย client 6 บานยังเป็นหน้าต่าง** ปุ่มในแผงรายละเอียดเปิดหน้าต่างเดิม ไม่ได้ยุบเนื้อหา
  เข้ามาไว้ในแผง — `RunningProcessesWindow` กับ `ActionReportWindow` รับสตรีมสดผ่าน SignalR และ
  `ActionPanelWindow` สั่งงานเป็นชุด การรื้อโครงต้องทดสอบกับ Agent จริงในห้องแล็บ โครงแผงพร้อมแล้ว
  ยุบทีละบานทีหลังได้โดยไม่ต้องแก้ส่วนอื่น
- **CheckBox / RadioButton / ComboBox ยังใช้สี accent ของ Windows (ฟ้า) ไม่ใช่สีส้มของเรา** เพราะยังใช้
  template ของ Fluent อยู่ ถ้าจะแก้ต้อง re-template เอง (มีสำรวจไว้แล้วว่า `Shadcn.Wpf` ภายใต้ MIT มี
  template ของสามตัวนี้ที่ลอกมาปรับสีได้ ประมาณครึ่งชั่วโมงต่อตัว)
- **ยังไม่ผ่านการทดสอบภาคสนาม** ทั้งหมดนี้ตรวจด้วยภาพเรนเดอร์จริงของทุกหน้าจอในทั้งสองธีม แต่ยังไม่มี
  ใครใช้กับเซิร์ฟเวอร์จริงและ Agent จริง
- **ผังห้อง (seat layout)** พักไว้ก่อนตามที่ตกลง ถ้าทำเมื่อไหร่ **ต้องเก็บผังที่ Server เท่านั้น** เพราะ
  เครื่องแล็บทุกเครื่องรัน Reboot Restore Rx ของที่เขียนลงดิสก์ฝั่ง client หายทุก reboot
- **RBAC (ซ่อนเมนูตามบทบาท)** ยังไม่ทำตามที่ตกลง
