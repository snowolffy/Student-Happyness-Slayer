# ประเมิน skill `dotnet-wpf-modern` สำหรับ Phase 1 (UI rebuild)

**Branch:** `feature/wpf-skill-eval` (ไม่แตะ `main`)
**สถานะ:** แผนงาน — ยังไม่ได้แก้โค้ด/ติดตั้ง skill จริง รอ approve ตามที่ขอ

---

## 1. เนื้อหา skill ที่ตรวจสอบแล้ว (ดึงไฟล์จริงมาอ่านทั้งไฟล์ ไม่ได้เชื่อคำอธิบายจาก prompt เดิม)

แหล่งที่มา: https://github.com/wshaddix/dotnet-skills/blob/master/skills/dotnet-wpf-modern/SKILL.md
(`raw.githubusercontent.com/wshaddix/dotnet-skills/master/skills/dotnet-wpf-modern/SKILL.md`)

Frontmatter ประกาศชัดเจนว่า: `"Building WPF on .NET 8+. Host builder, MVVM Toolkit, Fluent theme, performance, modern C# patterns."` และ **version assumption คือ baseline .NET 8.0+ (TFM `net8.0-windows`), ส่วน Fluent theme ต้อง .NET 9+** — ตรงกับที่ prompt สรุปมา ไม่ได้คลาดเคลื่อน

เนื้อหาเต็มมี 7 หมวด: SDK-style project template, Host Builder pattern (DI), MVVM Toolkit (`CommunityToolkit.Mvvm` source generators: `[ObservableProperty]`, `[RelayCommand]`), Performance (R2R/tiered compilation/trimming caveat), Modern C# (records, primary constructors, pattern matching, collection expressions), Theming (Fluent `ThemeMode` .NET 9+, system theme detection, custom ResourceDictionary themes), และ **Agent Gotchas 7 ข้อ**:

1. ห้ามใช้ pattern .NET Framework เดิม (`App.config`, `packages.config`, `ServiceLocator`, `AssemblyInfo.cs`)
2. ห้ามใช้ WPF API ที่เลิกใช้แล้ว (`BitmapEffect`, `DrawingContext.PushEffect`)
3. ห้ามผสม `{Binding}` กับ manual `INotifyPropertyChanged` เมื่อใช้ MVVM Toolkit อยู่แล้ว
4. **ห้ามใช้ `Dispatcher.Invoke` จาก async code** (อ้างว่า `await` marshal กลับ UI thread ให้เองอยู่แล้ว — เก็บ `Dispatcher.Invoke`/`BeginInvoke` ไว้ใช้เฉพาะ non-async context เช่น timer/COM callback)
5. ห้ามตั้ง `TrimMode=full` กับ WPF (ใช้ reflection เยอะ)
6. ห้ามลืม lifecycle ของ Host builder (`StartAsync`/`StopAsync`)
7. ห้าม hardcode สีตอนใช้ Fluent theme (ให้ใช้ `{DynamicResource SystemAccentColor}` แทน)

ยังตรวจสอบ skill อื่นในโฟลเดอร์เดียวกันที่ prompt เดิมสงสัยว่าอาจเกี่ยวข้อง (อ่าน frontmatter + scope boundary ของแต่ละตัว):

| skill | เกี่ยวข้องกับงานนี้ไหม | เหตุผล |
|---|---|---|
| `dotnet-wpf-migration` | **ไม่เกี่ยวข้อง** | คุมเฉพาะ migration จาก .NET Framework → .NET 8+ / WPF→WinUI/Uno โปรเจกต์นี้เป็น SDK-style + net10 อยู่แล้ว ไม่มีอะไรต้อง migrate |
| `dotnet-winui` | **ไม่เกี่ยวข้อง** | คุม WinUI 3 โดยเฉพาะ โปรเจกต์นี้ไม่ได้จะย้ายไป WinUI |
| `dotnet-ui-testing-core` | **ไม่เกี่ยวข้อง (สำหรับตอนนี้)** | เนื้อหาคุม page-object-model/test-selector สำหรับ Blazor/MAUI/Uno เท่านั้น "Brief guidance...cross-reference" — **ไม่มีเนื้อหา WPF โดยตรงเลย** และไม่ครอบคลุมงาน manual VNC session live-test ที่ค้างอยู่ (นั่นเป็น integration test ระดับระบบ ไม่ใช่ UI unit test) |
| `dotnet-accessibility` | **ไม่เกี่ยวข้อง (สำหรับ Phase 1)** | เนื้อหาลึกเฉพาะ Blazor/MAUI/WinUI ส่วน WPF ระบุเองว่าเป็นแค่ "brief guidance with cross-references" ไม่ได้เจาะลึก และโปรเจกต์นี้เป็นเครื่องมือ admin ภายในองค์กร ไม่ใช่ consumer-facing ที่มี compliance requirement |
| `dotnet-native-aot` | **ไม่เกี่ยวข้อง** | โปรเจกต์ผูกกับ native binary bundle (`VncBundle`, `TailscaleBundle`) และ reflection-heavy library (`Hardcodet.NotifyIcon.Wpf`, `System.Management`, `UseWindowsForms`) — Native AOT/trimming เสี่ยงพังโดยไม่เกี่ยวกับเป้าหมาย UI rebuild เลย |
| `dotnet-ui-chooser` | **ไม่เกี่ยวข้อง** | ตัดสินใจเลือก framework ไปแล้ว (WPF) ไม่ได้จะพิจารณาใหม่ |

**สรุป: มีแค่ `dotnet-wpf-modern` ตัวเดียวที่ตรงกับงานจริง ไม่แนะนำติดตั้งตัวอื่นเพิ่ม**

---

## 2. เทียบกับสถานะจริงของโปรเจกต์ (ตรวจโค้ดจริง ไม่เดา)

| หัวข้อใน skill | สมมติฐานของ skill | สถานะจริงในโปรเจกต์ | ผลสรุป |
|---|---|---|---|
| TargetFramework | `net8.0-windows` (baseline), .NET 9+ สำหรับ Fluent theme | **`net10.0-windows`** ทั้ง Console และ AgentTray ([OnionProcOparetor.Console.csproj:4](src/OnionProcOparetor.Console/OnionProcOparetor.Console.csproj#L4), [OnionProcOparetor.AgentTray.csproj:4](src/OnionProcOparetor.AgentTray/OnionProcOparetor.AgentTray.csproj#L4)) | โปรเจกต์ **ล้ำหน้ากว่า** สมมติฐานของ skill อยู่แล้ว — Fluent theme ใช้ได้ทันทีไม่ต้องอัปเกรดอะไร (ข้อกังวลเรื่อง .NET 8 vs 9 ใน prompt เดิม **ไม่จริง**) |
| Host Builder / DI | ใช้ `Host.CreateDefaultBuilder()` ผูก DI, config, `IHostedService` | **ไม่มีเลย** — `App.xaml.cs` ทั้ง Console และ AgentTray สร้าง service ด้วย `new Service()` ตรงๆ เป็น field ของ `App` class ([App.xaml.cs:14-19](src/OnionProcOparetor.AgentTray/App.xaml.cs#L14-L19)) | "ต้องปรับ" ถ้าจะเอามาใช้จริง — เป็นการ**เปลี่ยน architecture ทั้งแอป** ไม่ใช่แค่จุดเดียว |
| MVVM Toolkit | `ObservableObject` + `[ObservableProperty]`/`[RelayCommand]` ทุก ViewModel | **ไม่มี ViewModel แม้แต่ตัวเดียว** — ทุกหน้าต่างเป็น code-behind ล้วน (`.xaml.cs` จัดการ UI ตรงๆ) มี `INotifyPropertyChanged` แบบ manual แค่จุดเดียวคือ `ActionReportRow` สำหรับผูกกับ `DataGrid` ([ActionReportModels.cs:37](src/OnionProcOparetor.Console/Models/ActionReportModels.cs#L37)) — ไม่ได้ผสมกับ `{Binding}`+MVVM Toolkit เพราะไม่มี MVVM Toolkit อยู่เลย จึง**ไม่ชน gotcha ข้อ 3** | "ต้องปรับครั้งใหญ่" ถ้าจะนำมาใช้ — ไม่ใช่งานเสริม Phase 1 |
| Fluent theme (`ThemeMode`) | ตั้งใน `App.xaml` (`ThemeMode="System"`) | **ไม่มี** — `App.xaml` ของ Console ผูก custom ResourceDictionary เอง 3 ไฟล์ (`Theme/Colors.xaml`, `Typography.xaml`, `Controls.xaml` รวม ~440 บรรทัด) ส่วน AgentTray ผูก `Theme/AppTheme.xaml` (192 บรรทัด) — **มีระบบ theme มือเขียนอยู่แล้ว ไม่ใช่กระดาษเปล่า** | "ใช้ได้ตรงๆ" ด้าน prerequisite (.NET 10 ผ่านแน่นอน) แต่ "ต้องปรับ/ทดสอบ" ก่อนเปิดจริง เพราะ Fluent theme เปลี่ยน default control template ทั้งระบบ อาจชนกับ custom style ที่ลงทุนไปแล้วใน `Controls.xaml` |
| `Dispatcher.Invoke`/`BeginInvoke` gotcha #4 | "ห้ามใช้ใน async code เพราะ `await` marshal กลับ UI thread ให้เอง" | โปรเจกต์ใช้ `Dispatcher.Invoke`/`BeginInvoke` เยอะมาก **แต่ทุกจุดมี comment อธิบายเหตุผลไว้ชัดเจนแล้ว** ว่าทำไมต้องมี — ส่วนใหญ่เป็นการ marshal จาก **background thread ที่ไม่ใช่ async/await** (event callback จาก pipe listener, `VncProcessManager.Stopped`, `DispatcherTimer.Tick`) ไม่ใช่ marshal หลัง `await` ในเมธอด async ([App.xaml.cs:62,195,205](src/OnionProcOparetor.AgentTray/App.xaml.cs#L62), [DashboardWindow.xaml.cs](src/OnionProcOparetor.Console/DashboardWindow.xaml.cs)) | **gotcha นี้ไม่ตรงกับ pattern จริงของโปรเจกต์ — ห้ามเอาไปใช้แบบตรงตัว** ต้องอ่านทีละจุดว่าเรียกจาก async context จริงหรือจาก background-thread event callback (คนละเคสกัน) ไม่งั้นจะไปลบโค้ดที่จำเป็นออกโดยเข้าใจผิด |
| Records / primary constructors / pattern matching | แนะนำใช้แทน class/DTO เดิม | โปรเจกต์ยังใช้ `class` ธรรมดาสำหรับ DTO ส่วนใหญ่ (`RuleDto`, `ClientMachineDto` ฯลฯ) | "ใช้ได้" กับไฟล์ DTO ใหม่ที่จะสร้างใน Phase 1 — ความเสี่ยงต่ำ ทำเพิ่มทีละไฟล์ได้ไม่กระทบของเดิม |
| Native AOT / Trimming | แนะนำ `TrimMode=partial` | ไม่ได้ publish แบบ trim อยู่แล้ว ผูกกับ native bundle (VNC/Tailscale) | ข้ามได้ทั้งหมด ไม่เกี่ยวกับเป้าหมาย |

---

## 3. งานค้างจากรอบก่อนที่ user ระบุมา — เกี่ยวข้องกับ skill นี้แค่ไหน

- **RegisterConsole 1-arg signature (LabHub.cs) ต้อง live VNC test**: ไม่เกี่ยวกับ skill นี้เลย เป็น SignalR/networking ไม่ใช่ WPF UI pattern — skill `dotnet-ui-testing-core` ก็ไม่ครอบคลุม (ดูตารางข้อ 1) เพราะเป็น integration test ระดับ live session ไม่ใช่ UI test
- **ActionPanelWindow.xaml delay-seconds Grid row layout ต้อง visual inspection**: เกี่ยวข้องทางอ้อม — ถ้าจะเริ่มใช้ Fluent `ThemeMode` ควร **ทำ visual inspection ของ ActionPanelWindow ที่ค้างอยู่ให้เสร็จก่อน** แล้วค่อยเปิด ThemeMode เพราะ Fluent theme จะเปลี่ยนหน้าตา control ทุกตัวรวมถึงหน้าต่างนี้ด้วย — ถ้าทำพร้อมกันจะแยกไม่ออกว่า bug ไหนมาจาก Grid layout เดิม หรือมาจาก Fluent theme ใหม่

---

## 4. แผนติดตั้ง skill

**เลือก: ติดตั้งระดับ project (`.claude/skills/dotnet-wpf-modern/`) ไม่ใช่ global**

เหตุผล:
1. งานนี้เป็นการ**ทดลอง/ประเมิน** ตามที่ user ระบุเอง — ถ้าใช้ไม่ได้ผล ต้องถอดออกได้ง่ายโดยไม่กระทบโปรเจกต์อื่น
2. โปรเจกต์นี้มี pattern เฉพาะตัวที่ **ขัดกับ Agent Gotcha ของ skill ตรงๆ** อย่างน้อย 1 ข้อ (gotcha #4 เรื่อง `Dispatcher.Invoke` — ดูข้อ 2 ด้านบน) ถ้าติดตั้ง global แล้วไปใช้กับโปรเจกต์อื่นที่ไม่มี context นี้ ความเสี่ยงคือ agent ในโปรเจกต์อื่นจะเชื่อ gotcha แบบตรงตัวโดยไม่รู้ว่าโปรเจกต์นี้มีเหตุผลเฉพาะให้ทำตรงข้าม
3. โปรเจกต์นี้มีข้อจำกัดเฉพาะที่ skill ทั่วไปไม่รู้จัก เช่น disk freeze จาก Reboot Restore Rx บนเครื่องแล็บ 40 เครื่อง (runtime write หายทุกรีบูต) ซึ่งกระทบการตัดสินใจเรื่อง caching/settings persistence ถ้าจะทำ Host builder + config — ควรผูก guidance นี้ไว้กับโปรเจกต์ ไม่ใช่ปล่อยลอยเป็น global knowledge
4. ถ้าประเมินแล้วเวิร์คจริงหลัง Phase 1 เสร็จ ค่อย promote เป็น global ทีหลังได้ ไม่เสียของ

---

## 5. จุดที่จะเอาไปใช้ใน Phase 1 UI rebuild จริง (เรียงตาม risk/effort จากต่ำไปสูง)

1. **Modern C# patterns (records, pattern matching, collection expressions)** — ใช้กับไฟล์ DTO/Model ใหม่ที่สร้างระหว่าง Canvas→Grid conversion เท่านั้น ไม่แตะของเดิม (risk ต่ำสุด)
2. **Fluent theme evaluation** — ทดลองเปิด `ThemeMode="System"` ใน `App.xaml` ของ **Console อย่างเดียวก่อน** (ไม่ใช่ AgentTray เพราะ AgentTray มี LockScreenWindow ที่ต้องคุม visual แน่นๆ สำหรับ full-screen lock — เปลี่ยน theme กลางคันเสี่ยงกว่า) ต้องเทียบ before/after กับ `Controls.xaml` ที่มีอยู่ 352 บรรทัดว่าแตกหรือไม่ ก่อนตัดสินใจว่าจะ "แทนที่" หรือ "อยู่ร่วมกับ" custom theme เดิม
3. **Host builder + DI ใน Console/AgentTray** — เก็บไว้เป็นข้อเสนอสำหรับ Phase หลัง Phase 1 (ไม่ใช่ตอนนี้) เพราะเป็นการเปลี่ยน architecture ทั้งแอป กระทบทุกหน้าต่างที่มี `new Service()` อยู่ในตัวสร้าง ควรทำแยก PR ต่างหากจาก UI rebuild เพื่อไม่ปนกันตอน debug
4. **MVVM Toolkit rewrite** — **ไม่แนะนำใน Phase 1** ต้อง rewrite ทุกหน้าต่าง (~30 ไฟล์ `.xaml.cs`) เป็น View+ViewModel แยกกัน เกินขอบเขตของ "Canvas→Grid conversion ต่อ" มาก ถ้าจะทำควรเป็น Phase แยกที่มีการวางแผนเฉพาะ

---

## 6. ความเสี่ยง/ข้อควรระวัง

- **Gotcha #4 ของ skill (`Dispatcher.Invoke` ใน async code) ขัดกับโค้ดจริงของโปรเจกต์** — ถ้า apply แบบอัตโนมัติจะไปลบ marshal ที่จำเป็นออก (background-thread callback ไม่ใช่ post-await) ต้องอ่าน comment ที่มีอยู่แล้วทุกจุดก่อนแก้ ไม่เชื่อ skill 100%
- **Fluent theme อาจชนกับ custom theme ที่ลงทุนไปแล้ว** (Colors/Typography/Controls.xaml รวม ~440 บรรทัดใน Console, AppTheme.xaml 192 บรรทัดใน AgentTray) — ต้องทดสอบ visual ทุกหน้าต่างหลังเปิด ไม่ใช่แค่หน้าเดียว
- **AgentTray มีหน้าต่าง full-screen ที่ critical ต่อความปลอดภัย** (LockScreenWindow, ShutdownNotifyWindow) — การเปลี่ยน theme ต้องไม่กระทบ contrast/visibility ของหน้าจอล็อกที่นักเรียนต้องมองเห็นชัดเจน
- **Host builder + DI เป็นงานใหญ่ที่ไม่ควรปนกับ UI rebuild** — ถ้าทำพร้อมกันจะ debug ยากว่าปัญหาที่เจอมาจาก layout ใหม่หรือจาก DI wiring ใหม่
- Console ยังมีงานค้าง (ActionPanelWindow Grid layout ที่ต้อง visual inspection) — ควรปิดงานค้างนี้ก่อนเริ่มแตะ theme เพิ่ม ไม่งั้นจะแยกสาเหตุ bug ไม่ออก

---

## 7. ขั้นตอนถัดไปที่เสนอ (ยังไม่ลงมือทำ — รอ approve)

1. Approve การติดตั้ง `dotnet-wpf-modern` เป็น project-level skill (`.claude/skills/dotnet-wpf-modern/SKILL.md`) บน branch นี้
2. ปิดงานค้าง ActionPanelWindow.xaml (visual inspection) ให้เสร็จก่อน แยกเป็นงานของตัวเอง ไม่ปนกับ skill eval นี้
3. ทดลองเปิด `ThemeMode="System"` เฉพาะใน `OnionProcOparetor.Console/App.xaml` บน branch นี้ แล้ว build + เปิดดูทุกหน้าต่างจริงเทียบก่อน/หลัง ก่อนตัดสินใจว่าจะเก็บหรือ revert
4. ถ้า Fluent theme ดูดีและไม่ชนกับ `Controls.xaml` เดิม — ค่อยพิจารณาทำแบบเดียวกันกับ AgentTray แยกต่างหาก (ระวัง LockScreenWindow เป็นพิเศษ)
5. Host builder/DI และ MVVM Toolkit — เก็บเป็นข้อเสนอสำหรับ Phase ถัดไป ไม่ใช่ Phase 1 นี้ ไม่ต้อง approve ตอนนี้

ไม่มีการ merge กลับ `main` จนกว่าจะ approve ทีละขั้นตามนี้
