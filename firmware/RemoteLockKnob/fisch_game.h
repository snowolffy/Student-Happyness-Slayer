#ifndef FISCH_GAME_H
#define FISCH_GAME_H

// ============================================================================
// Fisch - เกมตกปลาในเมนู Others (ดู runOthersMenu ใน .ino หลัก) แยกไฟล์ออกมาเพราะเนื้อหาเยอะกว่า
// ของเล่นตัวอื่นมาก (state machine หลายเฟส + fish catalog + iris wipe transition ของตัวเอง)
//
// ข้อควรรู้สำคัญก่อนแก้ไฟล์นี้: ฟังก์ชันในไฟล์ .h ที่ #include เข้ามาใน .ino จะ**ไม่ถูก**สแกนหา
// prototype อัตโนมัติแบบเดียวกับฟังก์ชันที่เขียนตรงใน .ino เอง (เครื่องมือ auto-prototype ของ Arduino
// สแกนแค่เนื้อไฟล์ .ino ดิบๆ ไม่มองเข้าไปใน #include) เพราะงั้นถ้าฟังก์ชันหนึ่งในไฟล์นี้เรียกอีกฟังก์ชัน
// หนึ่งที่ยังไม่ถูกประกาศมาก่อนหน้านั้น**ในไฟล์นี้เอง** จะ compile ไม่ผ่าน - แก้ด้วยการประกาศ forward
// declaration ของทุกฟังก์ชันในไฟล์นี้ไว้เป็นก้อนเดียวด้านล่าง (ดู "FORWARD DECLARATIONS") ก่อนเนื้อหาจริง
// เพิ่มฟังก์ชันใหม่ = ต้องเพิ่ม prototype ในก้อนนั้นด้วยเสมอ ส่วนฟังก์ชัน/ตัวแปร global ที่ประกาศตรงใน
// .ino เอง (display, btnOk, playBeep, showDisplayMessage, Preferences ฯลฯ) เรียกจากที่นี่ได้ตามปกติ
// ไม่ต้อง forward declare เพราะ auto-prototype ของ Arduino จัดการให้แล้ว - **ต้อง #include ไฟล์นี้
// หลัง**จุดที่ประกาศ btnPrev/btnNext/btnOk/btnBack/playBeep ใน .ino หลัก (ตัวแปร ไม่เหมือนฟังก์ชัน
// ต้องประกาศก่อนใช้จริงเสมอ ไม่มี auto-prototype ช่วย) ดูจุด #include ใน .ino หลักที่คอมเมนต์ไว้
// ============================================================================

#include <math.h>
#include <string.h>

// ---------------------------------------------------------------------------
// Fish catalog - เพิ่มปลาใหม่ทีหลังได้แค่เพิ่ม element ในนี้ (+ ปรับ FISCH_CATALOG_COUNT อัตโนมัติจาก
// sizeof) ไม่ต้องแก้ state machine ที่ไหนเลย ชื่อเป็นภาษาอังกฤษล้วนเพราะฟอนต์ built-in ของ Adafruit_GFX
// ไม่มีตัวอักษรไทย (เหมือนข้อความอื่นทั้งหมดที่ขึ้นจอในไฟล์นี้)
// ---------------------------------------------------------------------------
enum FischRarity { FISCH_COMMON = 0, FISCH_UNCOMMON, FISCH_RARE, FISCH_EPIC, FISCH_LEGENDARY };
const char* const FISCH_RARITY_NAMES[] = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };

struct FischFishEntry {
  const char* name;
  FischRarity rarity;
  float minWeightKg;
  float maxWeightKg;
  uint8_t reelDifficulty; // 0-100 - ยิ่งสูงยิ่งขยับเร็ว/กระตุกแรงตอน Reel และหน้าต่างเวลา Hook สั้นลง
};

const FischFishEntry FISCH_CATALOG[] = {
  // Common (40)
  {"Tilapia", FISCH_COMMON, 0.05f, 0.49f, 8},
  {"Mackerel", FISCH_COMMON, 0.11f, 0.65f, 11},
  {"Catfish", FISCH_COMMON, 0.16f, 0.80f, 9},
  {"Snakehead", FISCH_COMMON, 0.22f, 0.96f, 13},
  {"Mud Carp", FISCH_COMMON, 0.28f, 0.77f, 11},
  {"Sardine", FISCH_COMMON, 0.33f, 0.92f, 10},
  {"Anchovy", FISCH_COMMON, 0.39f, 1.08f, 13},
  {"Silver Carp", FISCH_COMMON, 0.45f, 0.89f, 12},
  {"Grass Carp", FISCH_COMMON, 0.50f, 1.04f, 15},
  {"Bighead Carp", FISCH_COMMON, 0.56f, 1.20f, 14},
  {"Common Carp", FISCH_COMMON, 0.62f, 1.35f, 12},
  {"Crucian Carp", FISCH_COMMON, 0.67f, 1.17f, 16},
  {"Roach", FISCH_COMMON, 0.73f, 1.32f, 14},
  {"Bluegill", FISCH_COMMON, 0.79f, 1.48f, 18},
  {"Sunfish", FISCH_COMMON, 0.84f, 1.29f, 16},
  {"Perch", FISCH_COMMON, 0.90f, 1.44f, 14},
  {"Bream", FISCH_COMMON, 0.96f, 1.60f, 18},
  {"Rudd", FISCH_COMMON, 1.01f, 1.75f, 16},
  {"Minnow", FISCH_COMMON, 1.07f, 1.56f, 20},
  {"Goldfish", FISCH_COMMON, 1.13f, 1.72f, 18},
  {"Guppy", FISCH_COMMON, 1.18f, 1.87f, 17},
  {"Molly", FISCH_COMMON, 1.24f, 1.68f, 20},
  {"Herring", FISCH_COMMON, 1.30f, 1.84f, 19},
  {"Smelt", FISCH_COMMON, 1.35f, 1.99f, 22},
  {"Flounder", FISCH_COMMON, 1.41f, 2.15f, 21},
  {"Sole", FISCH_COMMON, 1.47f, 1.96f, 19},
  {"Whiting", FISCH_COMMON, 1.53f, 2.12f, 23},
  {"Pollock", FISCH_COMMON, 1.58f, 2.27f, 21},
  {"Haddock", FISCH_COMMON, 1.64f, 2.08f, 24},
  {"Cod", FISCH_COMMON, 1.70f, 2.24f, 23},
  {"Pilchard", FISCH_COMMON, 1.75f, 2.39f, 21},
  {"Gudgeon", FISCH_COMMON, 1.81f, 2.55f, 25},
  {"Loach", FISCH_COMMON, 1.87f, 2.36f, 23},
  {"Climbing Perch", FISCH_COMMON, 1.92f, 2.51f, 27},
  {"Walking Catfish", FISCH_COMMON, 1.98f, 2.67f, 25},
  {"Java Barb", FISCH_COMMON, 2.04f, 2.48f, 24},
  {"Silver Barb", FISCH_COMMON, 2.09f, 2.63f, 27},
  {"Tinfoil Barb", FISCH_COMMON, 2.15f, 2.79f, 26},
  {"Featherback", FISCH_COMMON, 2.21f, 2.94f, 29},
  {"Glass Fish", FISCH_COMMON, 2.26f, 2.75f, 28},
  // Uncommon (32)
  {"Seabass", FISCH_UNCOMMON, 0.20f, 1.37f, 27},
  {"Squid", FISCH_UNCOMMON, 0.39f, 1.82f, 30},
  {"Eel", FISCH_UNCOMMON, 0.58f, 2.27f, 29},
  {"Snakeskin Gourami", FISCH_UNCOMMON, 0.77f, 2.72f, 32},
  {"Giant Gourami", FISCH_UNCOMMON, 0.95f, 2.25f, 31},
  {"Kissing Gourami", FISCH_UNCOMMON, 1.14f, 2.70f, 29},
  {"Pearl Gourami", FISCH_UNCOMMON, 1.33f, 3.15f, 33},
  {"Red Tail Tinfoil", FISCH_UNCOMMON, 1.52f, 2.69f, 31},
  {"Rohu", FISCH_UNCOMMON, 1.71f, 3.14f, 35},
  {"Catla", FISCH_UNCOMMON, 1.90f, 3.59f, 33},
  {"Mrigal", FISCH_UNCOMMON, 2.09f, 4.04f, 32},
  {"Pangasius", FISCH_UNCOMMON, 2.28f, 3.58f, 35},
  {"Iridescent Shark", FISCH_UNCOMMON, 2.46f, 4.02f, 34},
  {"Small Redtail Catfish", FISCH_UNCOMMON, 2.65f, 4.47f, 37},
  {"Walleye", FISCH_UNCOMMON, 2.84f, 4.01f, 36},
  {"Pike", FISCH_UNCOMMON, 3.03f, 4.46f, 34},
  {"Muskellunge", FISCH_UNCOMMON, 3.22f, 4.91f, 38},
  {"Zander", FISCH_UNCOMMON, 3.41f, 5.36f, 36},
  {"Trout", FISCH_UNCOMMON, 3.60f, 4.90f, 40},
  {"Char", FISCH_UNCOMMON, 3.79f, 5.35f, 38},
  {"Grayling", FISCH_UNCOMMON, 3.97f, 5.79f, 37},
  {"Whitefish", FISCH_UNCOMMON, 4.16f, 5.33f, 40},
  {"Mullet", FISCH_UNCOMMON, 4.35f, 5.78f, 39},
  {"Garfish", FISCH_UNCOMMON, 4.54f, 6.23f, 42},
  {"Needlefish", FISCH_UNCOMMON, 4.73f, 6.68f, 41},
  {"Halfbeak", FISCH_UNCOMMON, 4.92f, 6.22f, 39},
  {"Triggerfish", FISCH_UNCOMMON, 5.11f, 6.67f, 43},
  {"Parrotfish", FISCH_UNCOMMON, 5.30f, 7.12f, 41},
  {"Snapper", FISCH_UNCOMMON, 5.48f, 6.65f, 45},
  {"Small Grouper", FISCH_UNCOMMON, 5.67f, 7.10f, 43},
  {"Small Barracuda", FISCH_UNCOMMON, 5.86f, 7.55f, 42},
  {"Mahi-mahi", FISCH_UNCOMMON, 6.05f, 8.00f, 46},
  // Rare (24)
  {"Koi Carp", FISCH_RARE, 1.00f, 4.60f, 44},
  {"Stingray", FISCH_RARE, 1.78f, 6.18f, 47},
  {"Electric Eel", FISCH_RARE, 2.57f, 7.77f, 46},
  {"Sturgeon", FISCH_RARE, 3.35f, 9.35f, 50},
  {"Paddlefish", FISCH_RARE, 4.13f, 8.13f, 48},
  {"Gar", FISCH_RARE, 4.91f, 9.71f, 47},
  {"Alligator Gar", FISCH_RARE, 5.70f, 11.30f, 51},
  {"Wels Catfish", FISCH_RARE, 6.48f, 10.08f, 49},
  {"Arapaima", FISCH_RARE, 7.26f, 11.66f, 53},
  {"Arowana", FISCH_RARE, 8.04f, 13.24f, 52},
  {"Peacock Bass", FISCH_RARE, 8.83f, 14.83f, 50},
  {"Tarpon", FISCH_RARE, 9.61f, 13.61f, 54},
  {"Bonefish", FISCH_RARE, 10.39f, 15.19f, 53},
  {"Permit", FISCH_RARE, 11.17f, 16.77f, 57},
  {"Giant Trevally", FISCH_RARE, 11.96f, 15.56f, 55},
  {"Yellowfin Tuna", FISCH_RARE, 12.74f, 17.14f, 54},
  {"Bluefin Tuna", FISCH_RARE, 13.52f, 18.72f, 58},
  {"Swordfish", FISCH_RARE, 14.30f, 20.30f, 56},
  {"Marlin", FISCH_RARE, 15.09f, 19.09f, 60},
  {"Sailfish", FISCH_RARE, 15.87f, 20.67f, 59},
  {"Blacktip Shark", FISCH_RARE, 16.65f, 22.25f, 57},
  {"Hammerhead Shark", FISCH_RARE, 17.43f, 21.03f, 61},
  {"Manta Ray", FISCH_RARE, 18.22f, 22.62f, 60},
  {"Goliath Grouper", FISCH_RARE, 19.00f, 24.20f, 64},
  // Epic (16)
  {"Giant Snakehead", FISCH_EPIC, 5.00f, 16.25f, 61},
  {"Giant Barb", FISCH_EPIC, 8.75f, 22.50f, 65},
  {"Giant Freshwater Stingray", FISCH_EPIC, 12.50f, 28.75f, 64},
  {"Redtail Catfish", FISCH_EPIC, 16.25f, 35.00f, 68},
  {"Piraiba Catfish", FISCH_EPIC, 20.00f, 32.50f, 67},
  {"Chinese Paddlefish", FISCH_EPIC, 23.75f, 38.75f, 66},
  {"Beluga Sturgeon", FISCH_EPIC, 27.50f, 45.00f, 70},
  {"Great White Shark", FISCH_EPIC, 31.25f, 42.50f, 69},
  {"Whale Shark", FISCH_EPIC, 35.00f, 48.75f, 74},
  {"Basking Shark", FISCH_EPIC, 38.75f, 55.00f, 73},
  {"Colossal Squid", FISCH_EPIC, 42.50f, 61.25f, 72},
  {"Giant Pacific Octopus", FISCH_EPIC, 46.25f, 58.75f, 76},
  {"Giant Moray Eel", FISCH_EPIC, 50.00f, 65.00f, 75},
  {"Ocean Sunfish", FISCH_EPIC, 53.75f, 71.25f, 79},
  {"Frilled Shark", FISCH_EPIC, 57.50f, 68.75f, 78},
  {"Six-Gill Shark", FISCH_EPIC, 61.25f, 75.00f, 78},
  // Legendary (8)
  {"Mekong Catfish", FISCH_LEGENDARY, 15.00f, 42.75f, 79},
  {"Oarfish", FISCH_LEGENDARY, 34.82f, 68.74f, 84},
  {"Coelacanth", FISCH_LEGENDARY, 54.64f, 94.73f, 84},
  {"Megamouth Shark", FISCH_LEGENDARY, 74.46f, 120.71f, 90},
  {"Giant Squid", FISCH_LEGENDARY, 94.29f, 125.12f, 90},
  {"White Sturgeon", FISCH_LEGENDARY, 114.11f, 151.11f, 91},
  {"Giant Freshwater Whipray", FISCH_LEGENDARY, 133.93f, 177.10f, 96},
  {"Greenland Shark", FISCH_LEGENDARY, 153.75f, 181.50f, 97},
};
const int FISCH_CATALOG_COUNT = sizeof(FISCH_CATALOG) / sizeof(FISCH_CATALOG[0]);

// ---------------------------------------------------------------------------
// ข้อมูลเซฟถาวร - เก็บเป็น blob เดียวผ่าน Preferences namespace "fisch" (putBytes/getBytes) แทนที่จะ
// แยกคีย์ต่อปลา (จะกลายเป็น 30+ คีย์) bestWeightKg[i] <= 0 หมายถึง "ยังไม่เคยจับชนิดนี้เลย" (ใช้แทน
// discovered flag แยก เพราะน้ำหนักปลาจริงเป็นบวกเสมออยู่แล้ว) heldCount คือจำนวนที่ถืออยู่ตอนนี้ (จะมี
// ผลตอนมี Shop ในอนาคต - ตอนนี้แค่โชว์เฉยๆ ใน Inventory)
// ---------------------------------------------------------------------------
struct FischSaveData {
  uint16_t heldCount[FISCH_CATALOG_COUNT];
  float bestWeightKg[FISCH_CATALOG_COUNT];
  uint32_t totalCasts;
  uint32_t totalCatches;
  uint32_t totalEscapes;
};

FischSaveData fischCache;

typedef void (*FischDrawFn)();

// ---------------------------------------------------------------------------
// สถานะ global ของเกม - ใช้แทนการส่ง parameter ข้ามฟังก์ชันเยอะๆ (ตรงกับสไตล์ที่ไฟล์ .ino หลักใช้อยู่
// แล้วทั้งไฟล์ เช่น selectedIndex, wifiConnectCancelled) เพราะ draw function ทุกตัวต้องมี signature
// ไม่รับ parameter เลย (void->void) ให้ตรงกับ FischDrawFn เพื่อใช้กับ fischTransitionTo ได้
// ---------------------------------------------------------------------------
bool fischQuitToMenu = false; // true = ผู้เล่นยืนยัน "Quit fishing?" ระหว่างเฟสใดก็ตาม - เช็คหลังเรียกทุกเฟส

int fischMenuIndex = 0;
const char* const FISCH_MAIN_ITEMS[] = { "Go Fishing", "Fish Log", "Inventory", "Stats", "Settings" };
const int FISCH_MAIN_ITEM_COUNT = 5;

bool fischLoadingReady = false;
int fischLoadingDots = 0;

float fischCastMarkerPos = 0;   // 0..100
int fischCastDir = 1;
int fischSweetStart = 0, fischSweetEnd = 0; // 0..100

int fischBiteRipplePhase = 0;
int fischCurrentFishIndex = -1;

int fischHookSide = 0; // -1=Get ready -2=ว่างสั้นๆหลังกดถูก -3=ว่างสั้นๆหลังพลาด(แต่ยังไม่หลุด) 0=left(<) 1=center(BACK) 2=right(>)
int fischHookHitsRequired = 0; // จำนวนครั้งที่ต้องกดถูก (1..30 สเกลตาม reelDifficulty)
int fischHookHitIndex = 0;     // กดถูกไปแล้วกี่ครั้ง
int fischHookMissesAllowed = 0; // โควตาพลาดได้ก่อนปลาหลุดจริง (hitsRequired/5) - สเกลตามความยากไปในตัว
int fischHookMissCount = 0;     // พลาดไปแล้วกี่ครั้ง (กดผิดปุ่ม/หมดเวลา) ยังไม่เกินโควตา = เล่นต่อได้
float fischHookTimeFrac = 0;    // 0..1 สัดส่วนเวลาที่ใช้ไปของหน้าต่างเวลาปัจจุบัน - ดูเส้นหดที่ drawFischHookFrame

float fischFishPos = 0.5f;     // 0..1 ตำแหน่งปลาบนแถบ reel
float fischFishVel = 0;
float fischCapturePos = 0.5f;  // 0..1 ตำแหน่งกลางแถบจับ
float fischCaptureVel = 0;     // ความเร็วปัจจุบันของแถบจับ (momentum) - ดู runFischReelPhase
float fischReelProgress = 0;   // 0..100
unsigned long fischReelZeroSinceMs = 0;

float fischCaughtWeight = 0;
bool fischCaughtIsNewSpecies = false;
bool fischCaughtIsNewBest = false;

int fischLogIndex = 0;
int fischInvIndex = 0;
int fischSettingsIndex = 0;
bool fischMomentumEnabled = true; // Settings > Reel Momentum - ปิดได้เผื่อใครชอบแถบ reel แบบแข็งๆดั้งเดิม

// ---------------------------------------------------------------------------
// ค่าคงที่ layout/จังหวะ - ปรับความยากง่าย/ตำแหน่งวาดได้ที่เดียวตรงนี้
// ---------------------------------------------------------------------------
const int FISCH_CX = SCREEN_WIDTH / 2;
const int FISCH_CY = SCREEN_HEIGHT / 2;
const int FISCH_MAX_RADIUS = 74; // เกินระยะมุมจอจริง (~71.6) นิดหน่อยกันขอบเหลือเศษพิกเซลตอนเปิดม่านเต็มที่
const int FISCH_WIPE_STEPS = 10;
const unsigned long FISCH_WIPE_STEP_MS = 22;

const int FISCH_CAST_BAR_X = 4, FISCH_CAST_BAR_Y = 46, FISCH_CAST_BAR_W = 120, FISCH_CAST_BAR_H = 8;
const unsigned long FISCH_CAST_TICK_MS = 20;
const int FISCH_CAST_SPEED = 3; // % ต่อ tick บนสเกล 0..100
const int FISCH_SWEET_MIN_HALF_WIDTH = 4;  // % - กว้างสุดเท่านี้ตอนดวงไม่ดี (สุ่มใหม่ทุกครั้งที่ cast)
const int FISCH_SWEET_MAX_HALF_WIDTH = 14; // % - กว้างสุดเท่านี้ตอนดวงดี

const unsigned long FISCH_BITE_TICK_MS = 100;

float fischCaptureHalfWidth = 0.11f; // สเกลตาม reelDifficulty ทุกครั้งที่เข้า Reel - ดู runFischReelPhase
const unsigned long FISCH_REEL_TICK_MS = 30;
const unsigned long FISCH_REEL_ESCAPE_GRACE_MS = 3000;
// ฟิสิกส์แถบจับ (ผู้เล่นคุม) - ใช้ความเร่ง+แรงโน้มถ่วง+เด้งแบบมี damping แทนการขยับตำแหน่งตรงๆทุก tick
// (แบบเดิมมันแข็งเพราะไม่มี momentum เลย) ดู runFischReelPhase
const float FISCH_CAPTURE_ACCEL = 2.6f;   // หน่วย/วิ² ตอนกด OK ค้าง (ดันเข้าหาปลา)
const float FISCH_CAPTURE_GRAVITY = 2.0f; // หน่วย/วิ² ตอนปล่อย (ดึงกลับ)
const float FISCH_CAPTURE_MAX_SPEED = 1.6f;
const float FISCH_BOUNCE_DAMPING = 0.55f; // ชนขอบแล้วเด้งกลับ แต่แรงลดลงทุกครั้ง ไม่ใช่สะท้อนเต็มแรงตลอดไป

const unsigned long FISCH_HOLD_REPEAT_DELAY_MS = 400;    // กดค้างนานแค่ไหนก่อนเริ่ม auto-repeat (Fish Log/Inventory)
const unsigned long FISCH_HOLD_REPEAT_INTERVAL_MS = 90;  // ทุกกี่ ms ต่อก้าวหลังจากนั้น

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS - ดูคอมเมนต์หัวไฟล์ว่าทำไมจำเป็น เพิ่มฟังก์ชันใหม่ในไฟล์นี้ = ต้องเพิ่มบรรทัดที่นี่ด้วย
// ---------------------------------------------------------------------------
void fischLoadSaveData();
void fischSaveSaveData();
int fischFindBestCatchIndex();

void fischMaskOutsideCircle(int r);
void fischIrisClose();
void fischIrisOpen(FischDrawFn drawFn);
void fischTransitionTo(FischDrawFn drawFn);

void drawFischConfirmOverlay(const char* question, int choice);
bool fischConfirmOverlay(const char* question);

void drawFischLoadingFrame();
void runFischLoading();

void fischPrintCentered(const String& text, int y);
String fischTruncateToFit(const String& text, int maxWidthPx);
void drawFischMainMenuFrame();
void runFischMainMenu();

FischRarity fischRollRarity(int castQuality);
int fischPickFishForRarity(FischRarity r);

void drawFischCastFrame();
int runFischCastPhase();

void drawFischBiteFrame();
void runFischBiteWaitPhase(int castQuality);

void drawFischHookFrame();
bool runFischHookReactionPhase();

void drawFischReelFrame();
bool runFischReelPhase();

void drawFischResultFrame();
void drawFischEscapeFrame();
void runFischFishingSequence();

bool fischHoldRepeatStep(Button& btn, unsigned long& holdStartMs, unsigned long& lastRepeatMs);

void drawFischLogFrame();
void runFischLogMenu();

int fischCountOwnedSpecies();
int fischOwnedCatalogIndex(int ownedIdx);
void drawFischInventoryFrame();
void runFischInventoryMenu();

void drawFischStatsFrame();
void runFischStatsPage();

void drawFischSettingsFrame();
void runFischSettingsMenu();

void runFischGame();

// ---------------------------------------------------------------------------
// Persistence (Preferences namespace "fisch") - อ่านครั้งเดียวตอนเข้าเกมเก็บไว้ใน fischCache แล้วให้
// ทุกหน้าจออ่านจาก cache ตรงๆ (ไม่เปิด Preferences ใหม่ทุกเฟรม ช้าและไม่จำเป็น) เขียนกลับ NVS เฉพาะตอน
// ข้อมูลเปลี่ยนจริง (จับปลาได้/cast/reset) getBytesLength ไม่ตรง sizeof = ยังไม่เคยเซฟ (บูตแรก) หรือ
// schema เปลี่ยน -> เริ่มจากค่าว่างทั้งหมดอย่างปลอดภัย แทนที่จะอ่านขยะ
// ---------------------------------------------------------------------------
void fischLoadSaveData() {
  Preferences prefs;
  prefs.begin("fisch", true);
  size_t len = prefs.getBytesLength("data");
  if (len == sizeof(FischSaveData)) {
    prefs.getBytes("data", &fischCache, sizeof(FischSaveData));
  } else {
    memset(&fischCache, 0, sizeof(FischSaveData));
  }
  // คีย์แยกต่างหากจาก blob "data" ข้างบน (ไม่ใช่ field ใน FischSaveData) - ตั้งใจแยกเพราะถ้าใส่ปนใน
  // struct แล้ววันหลังแก้ schema ของ struct อีก getBytesLength จะไม่ตรง sizeof แล้วทุกอย่างโดน reset
  // เป็นศูนย์หมด (รวมสถิติ/ปลาที่เคยจับ) ทั้งที่แค่อยากแก้ toggle ตัวเดียว
  fischMomentumEnabled = prefs.getBool("momentumOn", true);
  prefs.end();
}

void fischSaveSaveData() {
  Preferences prefs;
  prefs.begin("fisch", false);
  prefs.putBytes("data", &fischCache, sizeof(FischSaveData));
  prefs.end();
}

/// ปลา "เด่น" โชว์หน้า main menu = rarity สูงสุดที่เคยจับได้ (เสมอกันตัดสินด้วยน้ำหนัก) คืน -1 ถ้ายังไม่เคยจับเลย
int fischFindBestCatchIndex() {
  int bestIdx = -1;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    if (fischCache.bestWeightKg[i] <= 0.0f) continue;
    if (bestIdx < 0) { bestIdx = i; continue; }
    if (FISCH_CATALOG[i].rarity > FISCH_CATALOG[bestIdx].rarity) {
      bestIdx = i;
    } else if (FISCH_CATALOG[i].rarity == FISCH_CATALOG[bestIdx].rarity &&
               fischCache.bestWeightKg[i] > fischCache.bestWeightKg[bestIdx]) {
      bestIdx = i;
    }
  }
  return bestIdx;
}

// ---------------------------------------------------------------------------
// Iris wipe transition - วงกลมดำหด/ขยายจากกลางจอ ใช้ได้ทุกจุดเปลี่ยนหน้าใน Fisch ไม่อ่าน pixel กลับจากจอ
// เลย (จอ SSD1306 ไม่มี alpha/สีที่ 3 - เทคนิคคือวาดดำทับ "นอกวงกลม" เท่านั้น ปล่อยข้างในวงกลมไว้เฉยๆ ไม่
// ต้องรู้ว่าข้างในมีอะไรอยู่) ปิดม่าน (close) ทับเนื้อหาเดิมที่ค้างอยู่บนจอได้เลยตรงๆ ส่วนเปิดม่าน (open)
// ต้องวาดเนื้อหาใหม่เต็มจอซ้ำทุกเฟรมของ animation (ผ่าน drawFn ที่ไม่เรียก display.display() เอง) แล้ว
// ค่อย mask ทับ - เพราะงั้น draw function ทุกตัวในไฟล์นี้ **ห้ามเรียก display.display() เอง** ต่างจาก
// convention ของฟังก์ชันวาดจอในไฟล์ .ino หลัก (เช่น drawFlappyFrame) ที่เรียกเองตอนจบฟังก์ชันเสมอ - ผู้
// เรียก (run*Phase/run*Menu) เป็นคนสั่ง display.display() เองหลัง draw ทุกครั้งแทน
// ---------------------------------------------------------------------------
void fischMaskOutsideCircle(int r) {
  if (!displayReady) return;
  for (int y = 0; y < SCREEN_HEIGHT; y++) {
    int dy = y - FISCH_CY;
    if (dy * dy >= r * r) {
      display.drawFastHLine(0, y, SCREEN_WIDTH, SSD1306_BLACK);
    } else {
      int dx = (int)sqrt((float)(r * r - dy * dy));
      int leftEnd = FISCH_CX - dx;
      int rightStart = FISCH_CX + dx + 1;
      if (leftEnd > 0) display.drawFastHLine(0, y, leftEnd, SSD1306_BLACK);
      if (rightStart < SCREEN_WIDTH) display.drawFastHLine(rightStart, y, SCREEN_WIDTH - rightStart, SSD1306_BLACK);
    }
  }
}

void fischIrisClose() {
  if (!displayReady) return;
  for (int step = 0; step <= FISCH_WIPE_STEPS; step++) {
    int r = FISCH_MAX_RADIUS - (FISCH_MAX_RADIUS * step / FISCH_WIPE_STEPS);
    fischMaskOutsideCircle(r);
    display.display();
    delay(FISCH_WIPE_STEP_MS);
  }
}

void fischIrisOpen(FischDrawFn drawFn) {
  if (!displayReady) { drawFn(); return; }
  for (int step = 0; step <= FISCH_WIPE_STEPS; step++) {
    int r = FISCH_MAX_RADIUS * step / FISCH_WIPE_STEPS;
    drawFn();
    fischMaskOutsideCircle(r);
    display.display();
    delay(FISCH_WIPE_STEP_MS);
  }
}

/// ปิดม่านทับหน้าเดิมที่ค้างอยู่บนจอ แล้วเปิดม่านเข้าสู่หน้าใหม่ที่ drawFn วาด - ใช้จุดเดียวนี้ทุกครั้งที่
/// เปลี่ยน "หน้าเต็มจอ" ใน Fisch (ไม่ใช้กับ confirm overlay ซึ่งเป็นแค่กล่องทับหน้าเดิม ไม่ใช่เปลี่ยนหน้า)
void fischTransitionTo(FischDrawFn drawFn) {
  fischIrisClose();
  fischIrisOpen(drawFn);
}

// ---------------------------------------------------------------------------
// Confirm overlay - กล่องถาม Yes/No ทับหน้าจอเดิมตรงๆ (ไม่ clearDisplay ไม่ iris wipe) ใช้ทั้งตอนกด BACK
// ระหว่างตกปลา ("Quit fishing?") และตอน Settings > Reset all data - ค่าเริ่มต้นเลือกไว้ที่ "No" เสมอกัน
// กดมือลั่นแล้วเสียของ ผู้เรียกต้อง redraw หน้าเดิมเองหลังฟังก์ชันนี้คืนค่า false (กล่องทับพื้นที่บางส่วนไว้)
// ---------------------------------------------------------------------------
void drawFischConfirmOverlay(const char* question, int choice) {
  if (!displayReady) return;
  display.fillRect(8, 16, 112, 34, SSD1306_BLACK);
  display.drawRect(8, 16, 112, 34, SSD1306_WHITE);
  display.setTextSize(1);
  display.setCursor(14, 22);
  display.print(question);
  display.setCursor(20, 38);
  display.print(choice == 0 ? "> Yes    No" : "  Yes  > No");
}

bool fischConfirmOverlay(const char* question) {
  if (!displayReady) return true;

  while (btnOk.isPressed() || btnBack.isPressed() || btnNext.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnNext.lastPressed = false;
  btnPrev.lastPressed = false;

  int choice = 1; // default "No"
  drawFischConfirmOverlay(question, choice);
  display.display();

  while (true) {
    if (btnNext.consumeJustPressed() || btnPrev.consumeJustPressed()) {
      choice = 1 - choice;
      drawFischConfirmOverlay(question, choice);
      display.display();
    }
    if (btnBack.consumeJustPressed()) return false; // กด BACK ซ้ำระหว่างถาม = ยกเลิก เหมือนเลือก No
    if (btnOk.consumeJustPressed()) return choice == 0;
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Loading screen - ตัดสินใจแล้วว่าจะไม่วาด sprite/รูปเลย (text-only ดู classic ดีอยู่แล้ว ไม่ใช่แค่ "ยัง
// ไม่มีเวลาวาด" แบบตอนแรก) เอาชื่อเกมวางตรงจุดที่ปลาจะอยู่แทน จุดไข่ปลาขยับได้ด้วย logic ง่ายๆ
// ---------------------------------------------------------------------------
void drawFischLoadingFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(2);
  display.setCursor(28, 16);
  display.print("FISCH");
  display.setTextSize(1);
  display.setCursor(30, 40);
  if (fischLoadingReady) {
    display.print("Press OK");
  } else {
    display.print("Loading");
    for (int i = 0; i < fischLoadingDots; i++) display.print(".");
  }
}

void runFischLoading() {
  fischLoadingReady = false;
  fischLoadingDots = 0;
  fischTransitionTo(drawFischLoadingFrame);

  const unsigned long LOAD_DURATION_MS = 1200;
  unsigned long startT = millis();
  unsigned long lastTick = startT;
  while (millis() - startT < LOAD_DURATION_MS) {
    unsigned long now = millis();
    if (now - lastTick >= 250) {
      lastTick = now;
      fischLoadingDots = (fischLoadingDots + 1) % 4;
      drawFischLoadingFrame();
      display.display();
    }
    delay(10);
  }

  fischLoadingReady = true;
  drawFischLoadingFrame();
  display.display();

  while (btnOk.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  while (!btnOk.consumeJustPressed()) { delay(20); }
}

// ---------------------------------------------------------------------------
// Main menu - แสดงปลาเด่น (best catch) เป็น text แทนที่จะวาดปลาว่ายพื้นหลังตามที่ตกลงกันไว้ (ไม่มี sprite)
// ทุกบรรทัดจัดกึ่งกลางแนวนอน (คำนวณความกว้าง text จริงผ่าน getTextBounds เพราะความยาวชื่อปลา/เมนูไม่คงที่
// ใช้ x คงที่แบบเดิมจะเบี้ยว) ส่วนแนวตั้งเกลี่ย title/best/selection ให้เป็นกลุ่มก้อนกลางจอ เว้น footer
// ไว้ที่ y=56 เหมือนหน้าจออื่นทั้งไฟล์ (บรรทัดคำสั่งปุ่มอยู่ล่างสุดเสมอ)
// ---------------------------------------------------------------------------
/// พิมพ์ text หนึ่งบรรทัดให้อยู่กึ่งกลางแนวนอนพอดีของจอ ณ ความสูง y ที่กำหนด - ใช้ String เพื่อรองรับ
/// ข้อความที่ต่อกันแบบ dynamic (เช่น "Best: " + ชื่อปลา) ได้ตรงๆ ไม่ต้องวุ่นกับ char buffer เอง
void fischPrintCentered(const String& text, int y) {
  if (!displayReady) return;
  int16_t x1, y1;
  uint16_t w, h;
  display.getTextBounds(text, 0, y, &x1, &y1, &w, &h);
  int x = (SCREEN_WIDTH - (int)w) / 2;
  if (x < 0) x = 0;
  display.setCursor(x, y);
  display.print(text);
}

/// ตัดท้าย text ด้วย "..." ถ้ากว้างเกิน maxWidthPx (บางชื่อปลายาวเกิน 21 ตัวอักษรที่จอ 128px รับได้ที่
/// text size 1 - ไม่ตัดแบบนี้ default text wrap ของ Adafruit_GFX จะตัดกลางคำแล้วตกไปบรรทัดถัดไปแทน ซึ่ง
/// ทับ layout ที่วางไว้ + fischPrintCentered คำนวณกึ่งกลางผิดด้วยถ้าข้อความกลายเป็น 2 บรรทัด) ไม่กระทบ
/// ข้อความสั้นที่พอดีอยู่แล้ว - คืนค่าเดิมตรงๆถ้าไม่เกิน
String fischTruncateToFit(const String& text, int maxWidthPx) {
  if (!displayReady) return text;
  int16_t x1, y1;
  uint16_t w, h;
  display.getTextBounds(text, 0, 0, &x1, &y1, &w, &h);
  if ((int)w <= maxWidthPx) return text;

  String t = text;
  while (t.length() > 1) {
    t.remove(t.length() - 1);
    String candidate = t + "...";
    display.getTextBounds(candidate, 0, 0, &x1, &y1, &w, &h);
    if ((int)w <= maxWidthPx) return candidate;
  }
  return t;
}

void drawFischMainMenuFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  fischPrintCentered("=== FISCH ===", 6);

  int bestIdx = fischFindBestCatchIndex();
  String bestLine = bestIdx < 0 ? String("No fish yet") : String("Best: ") + FISCH_CATALOG[bestIdx].name;
  fischPrintCentered(fischTruncateToFit(bestLine, SCREEN_WIDTH - 4), 20);

  fischPrintCentered(String("> ") + FISCH_MAIN_ITEMS[fischMenuIndex], 34);

  fischPrintCentered("<> OK BACK", 56);
}

void runFischMainMenu() {
  fischMenuIndex = 0;
  fischTransitionTo(drawFischMainMenuFrame);

  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischMenuIndex = (fischMenuIndex + 1) % FISCH_MAIN_ITEM_COUNT;
      drawFischMainMenuFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischMenuIndex = (fischMenuIndex - 1 + FISCH_MAIN_ITEM_COUNT) % FISCH_MAIN_ITEM_COUNT;
      drawFischMainMenuFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) {
      Serial.println("[Fisch] ออกกลับเมนู Others");
      return;
    }
    if (btnOk.consumeJustPressed()) {
      if (fischMenuIndex == 0) {
        runFischFishingSequence();
      } else if (fischMenuIndex == 1) {
        runFischLogMenu();
      } else if (fischMenuIndex == 2) {
        runFischInventoryMenu();
      } else if (fischMenuIndex == 3) {
        runFischStatsPage();
      } else if (fischMenuIndex == 4) {
        runFischSettingsMenu();
      }
      fischTransitionTo(drawFischMainMenuFrame);
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// สุ่ม rarity/ชนิดปลา - castQuality (0-100 จาก Phase 1) เอียงผลไปทางของหายากมากขึ้น cast แม่นยิ่งมีโอกาส
// เจอ Epic/Legendary สูงขึ้นชัดเจน (cast แย่สุด = แทบเป็นไปไม่ได้เลยที่จะเจอของหายาก) ตัวเลข threshold
// ปรับจูนได้ตรงนี้ที่เดียวถ้าอยากเปลี่ยนสัดส่วนทีหลัง
// ---------------------------------------------------------------------------
FischRarity fischRollRarity(int castQuality) {
  int bonus = castQuality / 2; // 0..50
  int roll = random(0, 100) + bonus; // 0..149
  if (roll >= 130) return FISCH_LEGENDARY;
  if (roll >= 100) return FISCH_EPIC;
  if (roll >= 75) return FISCH_RARE;
  if (roll >= 45) return FISCH_UNCOMMON;
  return FISCH_COMMON;
}

int fischPickFishForRarity(FischRarity r) {
  int candidates[FISCH_CATALOG_COUNT];
  int count = 0;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    if (FISCH_CATALOG[i].rarity == r) candidates[count++] = i;
  }
  if (count == 0) return 0;
  return candidates[random(0, count)];
}

// ---------------------------------------------------------------------------
// Phase 1: Cast - แถบพลัง/จังหวะ marker วิ่งซ้าย-ขวาไปมา กด OK หยุดตรง sweet spot ให้แม่นที่สุด quality
// (0-100) สูง = รอบัดสั้นลง + โอกาสเจอปลาหายากสูงขึ้น (ดู fischRollRarity) ไม่มีทาง "fail" ตรงนี้ ได้
// quality แค่ไหนก็ยิงต่อได้เสมอ - กด BACK ได้ทุกเมื่อ เปิด confirm overlay (ทุกเฟสของการตกปลาเหมือนกันหมด)
// ---------------------------------------------------------------------------
void drawFischCastFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Cast! Press OK");
  display.setCursor(0, 20);
  display.print("BACK = quit");

  display.drawRect(FISCH_CAST_BAR_X, FISCH_CAST_BAR_Y, FISCH_CAST_BAR_W, FISCH_CAST_BAR_H, SSD1306_WHITE);

  int sweetX = FISCH_CAST_BAR_X + (fischSweetStart * FISCH_CAST_BAR_W) / 100;
  int sweetW = ((fischSweetEnd - fischSweetStart) * FISCH_CAST_BAR_W) / 100;
  display.fillRect(sweetX, FISCH_CAST_BAR_Y, sweetW, FISCH_CAST_BAR_H, SSD1306_WHITE);

  int markerX = FISCH_CAST_BAR_X + (int)((fischCastMarkerPos * FISCH_CAST_BAR_W) / 100.0f);
  display.drawFastVLine(markerX, FISCH_CAST_BAR_Y - 3, FISCH_CAST_BAR_H + 6, SSD1306_WHITE);
}

int runFischCastPhase() {
  fischCastMarkerPos = 0;
  fischCastDir = 1;
  int sweetCenter = random(15, 85);
  // สุ่มความกว้างของ sweet spot ด้วย ไม่ใช่แค่ตำแหน่ง - กว้างแค่ไหนก็เป็นเรื่องดวงเหมือนตำแหน่ง ยิ่งกว้าง
  // ยิ่งง่ายที่จะหยุด marker ได้แม่น -> quality สูง -> โอกาสเจอปลาเรทสูงมากขึ้นไปด้วย (ดู fischRollRarity)
  int sweetHalfWidth = random(FISCH_SWEET_MIN_HALF_WIDTH, FISCH_SWEET_MAX_HALF_WIDTH + 1);
  fischSweetStart = sweetCenter - sweetHalfWidth;
  fischSweetEnd = sweetCenter + sweetHalfWidth;
  if (fischSweetStart < 0) fischSweetStart = 0;
  if (fischSweetEnd > 100) fischSweetEnd = 100;

  fischCache.totalCasts++;
  fischSaveSaveData();

  fischTransitionTo(drawFischCastFrame);
  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  unsigned long lastTick = millis();
  while (true) {
    if (btnBack.consumeJustPressed()) {
      if (fischConfirmOverlay("Quit fishing?")) {
        fischQuitToMenu = true;
        return 0;
      }
      drawFischCastFrame();
      display.display();
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }
    if (btnOk.consumeJustPressed()) {
      int center = (fischSweetStart + fischSweetEnd) / 2;
      int dist = abs((int)fischCastMarkerPos - center);
      int halfRange = max(center, 100 - center);
      int quality = 100 - (dist * 100) / max(halfRange, 1);
      if (quality < 0) quality = 0;
      if (quality > 100) quality = 100;
      playBeep(40);
      return quality;
    }
    unsigned long now = millis();
    if (now - lastTick >= FISCH_CAST_TICK_MS) {
      lastTick = now;
      fischCastMarkerPos += fischCastDir * FISCH_CAST_SPEED;
      if (fischCastMarkerPos >= 100) { fischCastMarkerPos = 100; fischCastDir = -1; }
      if (fischCastMarkerPos <= 0) { fischCastMarkerPos = 0; fischCastDir = 1; }
      drawFischCastFrame();
      display.display();
    }
    delay(5);
  }
}

// ---------------------------------------------------------------------------
// Phase 2: รอกัด - เลือกชนิดปลาตรงนี้เลย (ก่อนเห็นผล Hook/Reel) เก็บไว้ที่ fischCurrentFishIndex ให้เฟส
// ถัดๆไปอ่านความยาก (reelDifficulty) ไปคำนวณต่อ ทุ่นลอยน้ำวาดด้วย primitive shapes ล้วน (วงกลม+เส้น) ไม่ใช่
// sprite - รอสุ่มเวลา สั้นลงถ้า cast quality สูง
// ---------------------------------------------------------------------------
void drawFischBiteFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Waiting for a bite...");
  display.setCursor(0, 56);
  display.print("BACK = quit");

  int cx = 64, cy = 40;
  display.drawFastHLine(20, cy + 6, 88, SSD1306_WHITE); // เส้นผิวน้ำ
  int r = 2 + fischBiteRipplePhase;
  display.drawCircle(cx, cy, r, SSD1306_WHITE);
  display.fillCircle(cx, cy - 6, 2, SSD1306_WHITE); // ทุ่นลอย
}

void runFischBiteWaitPhase(int castQuality) {
  FischRarity rarity = fischRollRarity(castQuality);
  fischCurrentFishIndex = fischPickFishForRarity(rarity);

  long waitMs = random(2200, 4500) - (castQuality * 18L);
  if (waitMs < 800) waitMs = 800;

  fischBiteRipplePhase = 0;
  fischTransitionTo(drawFischBiteFrame);
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  unsigned long startT = millis();
  unsigned long lastTick = startT;
  while ((long)(millis() - startT) < waitMs) {
    if (btnBack.consumeJustPressed()) {
      if (fischConfirmOverlay("Quit fishing?")) {
        fischQuitToMenu = true;
        return;
      }
      drawFischBiteFrame();
      display.display();
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }
    unsigned long now = millis();
    if (now - lastTick >= FISCH_BITE_TICK_MS) {
      lastTick = now;
      fischBiteRipplePhase = (fischBiteRipplePhase + 1) % 4;
      drawFischBiteFrame();
      display.display();
    }
    delay(10);
  }
  playBeepPattern(2, 50, 50); // ปลากัด!
}

// ---------------------------------------------------------------------------
// Phase 3: Hook reaction - ไอคอนโผล่ซ้าย/กลาง/ขวา ต้องกด < / กลาง / > ให้ตรงตำแหน่งภายในเวลาที่กำหนด
// (สั้นลงตามความยากของปลา) **ต้องกดถูกติดต่อกันหลายครั้ง** ไม่ใช่แค่ครั้งเดียว (เหมือนเกมจริง) จำนวนครั้ง
// (fischHookHitsRequired) สเกล 1..30 ตาม reelDifficulty ของปลาที่สุ่มไว้ตั้งแต่ Phase 2 เข้าหน้านี้ครั้งแรก
// จะค้างไว้ 1 วิ (โชว์ "Get ready...") ให้ fade จบและตั้งตัวก่อน ค่อยเริ่มจับเวลาจริง
//
// **มีโควตาพลาดได้ (fischHookMissesAllowed = hitsRequired/5)** ไม่ใช่พลาดครั้งเดียวหลุดทันทีแบบเดิม -
// ปลายากต้องกดถึง 30 ครั้งติด ถ้าไม่มีโควตาเผื่อเลยแทบจะเป็นไปไม่ได้ที่จะจับได้จริง พลาดในโควตายังนับ
// hitIndex ต่อไม่ได้ (ต้องกดถูกครบ hitsRequired จริงๆ) แค่ไม่ทำให้ปลาหลุด เกินโควตาเมื่อไหร่ถึงหลุดจริง
//
// **หน้านี้หน้าเดียวในทั้งเกม Fisch ที่ role ของ SW_OK กับ BACK สลับกัน** ตามที่ผู้ใช้ขอ: ตำแหน่งกลาง
// ตอบด้วย BACK แทน SW_OK ส่วน "Quit fishing?" เปิดด้วย SW_OK แทน BACK - ทุกเฟสอื่นของ Fisch (Cast/Bite/
// Reel) ยังใช้ BACK = quit ตามปกติเหมือนเดิมทุกที่
// ---------------------------------------------------------------------------
void drawFischHookFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  if (fischHookSide == -1) {
    fischPrintCentered("Get ready...", 28);
    return;
  }

  display.setCursor(0, 0);
  display.print("Hit ");
  display.print(fischHookHitIndex + 1);
  display.print("/");
  display.print(fischHookHitsRequired);

  if (fischHookMissesAllowed > 0) {
    display.setCursor(70, 0);
    display.print("Miss ");
    display.print(fischHookMissCount);
    display.print("/");
    display.print(fischHookMissesAllowed);
  }

  if (fischHookSide == -2) return; // ว่างสั้นๆ หลังกดถูก - โชว์แค่ตัวนับ ไม่มีไอคอน

  if (fischHookSide == -3) {
    fischPrintCentered("Missed!", 34); // พลาดแต่ยังไม่เกินโควตา - บอกให้รู้ว่าพลาดแล้ว ไม่ใช่ค้าง
    return;
  }

  int y = 34;
  display.setTextSize(2);
  if (fischHookSide == 0) {
    display.setCursor(14, y);
    display.print("<");
  } else if (fischHookSide == 1) {
    display.setCursor(58, y);
    display.print("O");
  } else {
    display.setCursor(104, y);
    display.print(">");
  }
  display.setTextSize(1);

  // เส้นบอกเวลาเหลือ - ตอนเพิ่งขึ้นเป็นเส้นเต็มความกว้าง แล้วค่อยๆหดเข้าหากลางจากทั้ง 2 ฝั่ง เหลือ 0 พอดี
  // ตอนหมดเวลา (fischHookTimeFrac วิ่ง 0..1 อัปเดตทุก ~30ms ในลูปรอกดที่ runFischHookReactionPhase)
  int inset = (int)(fischHookTimeFrac * (SCREEN_WIDTH / 2));
  int lineStart = inset;
  int lineEnd = SCREEN_WIDTH - inset;
  if (lineEnd > lineStart) {
    display.drawFastHLine(lineStart, 60, lineEnd - lineStart, SSD1306_WHITE);
  }
}

bool runFischHookReactionPhase() {
  int diff = FISCH_CATALOG[fischCurrentFishIndex].reelDifficulty;
  fischHookHitsRequired = 1 + (diff * 29) / 100; // diff 0..100 -> 1..30 ครั้ง
  fischHookHitIndex = 0;
  fischHookMissesAllowed = fischHookHitsRequired / 5; // ทุก 5 ครั้งที่ต้องกด เผื่อพลาดได้ 1
  fischHookMissCount = 0;
  long windowMs = 900 - (diff * 5L);
  if (windowMs < 300) windowMs = 300;

  fischHookSide = -1; // โชว์ "Get ready..." ระหว่าง fade เข้า + ค้างต่ออีกหน่อยหลังจากนั้น
  fischTransitionTo(drawFischHookFrame);
  delay(1000);

  while (btnOk.isPressed() || btnBack.isPressed() || btnNext.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnNext.lastPressed = false;
  btnPrev.lastPressed = false;

  while (fischHookHitIndex < fischHookHitsRequired) {
    fischHookSide = random(0, 3);
    fischHookTimeFrac = 0;
    drawFischHookFrame();
    display.display();

    unsigned long startT = millis();
    unsigned long lastDrawTick = startT;
    bool gotHit = false;
    while ((long)(millis() - startT) < windowMs) {
      // สลับกับทุกเฟสอื่น: หน้านี้ SW_OK เป็นตัวเปิด "Quit fishing?" แทน BACK (ดูคอมเมนต์หัวฟังก์ชัน)
      if (btnOk.consumeJustPressed()) {
        if (fischConfirmOverlay("Quit fishing?")) {
          fischQuitToMenu = true;
          return false;
        }
        startT = millis(); // ให้เวลาใหม่หลังกลับมาเล่นต่อ ไม่นับเวลาที่หยุดดู dialog รวมด้วย
        lastDrawTick = startT;
        fischHookTimeFrac = 0;
        drawFischHookFrame();
        display.display();
        while (btnOk.isPressed()) { delay(20); }
        btnOk.lastPressed = false;
      }
      bool pl = btnPrev.consumeJustPressed();
      bool pc = btnBack.consumeJustPressed(); // สลับ: ตำแหน่งกลางตอบด้วย BACK แทน SW_OK ในหน้านี้
      bool pr = btnNext.consumeJustPressed();
      if (pl || pc || pr) {
        int pressedSide = pl ? 0 : (pc ? 1 : 2);
        gotHit = (pressedSide == fischHookSide);
        break;
      }

      unsigned long now = millis();
      if (now - lastDrawTick >= 30) {
        lastDrawTick = now;
        fischHookTimeFrac = (float)(now - startT) / (float)windowMs;
        if (fischHookTimeFrac > 1) fischHookTimeFrac = 1;
        drawFischHookFrame();
        display.display();
      }
      delay(5);
    }

    if (gotHit) {
      fischHookHitIndex++;
      // ว่างสั้นๆ ก่อนโชว์ไอคอนครั้งถัดไป - กันกรณีด้านถัดไปสุ่มออกมาตำแหน่งเดิมซ้ำ (ซ้าย/กลาง/ขวา 1 ใน 3
      // มีโอกาสซ้ำเสมอ) แล้วผู้เล่นมองไม่ออกว่าเพิ่งกดผ่านไปแล้วจริงๆ หรือค้างอยู่ที่เดิม
      if (fischHookHitIndex < fischHookHitsRequired) {
        fischHookSide = -2;
        drawFischHookFrame();
        display.display();
        delay(180);
      }
    } else {
      // กดผิดปุ่มหรือหมดเวลา - ถ้ายังไม่เกินโควตาพลาด ให้อภัยแล้วสุ่มไอคอนใหม่ต่อ (hitIndex ไม่ขยับ ยังต้อง
      // กดถูกให้ครบ hitsRequired เหมือนเดิม) เกินโควตาเมื่อไหร่ค่อยปลาหลุดจริง
      fischHookMissCount++;
      if (fischHookMissCount > fischHookMissesAllowed) {
        return false;
      }
      fischHookSide = -3;
      drawFischHookFrame();
      display.display();
      delay(350);
    }
  }
  return true; // กดถูกครบทุกครั้งที่ต้องการ
}

// ---------------------------------------------------------------------------
// Phase 4: Reel - แนว Stardew Valley: ปลาขยับเองแบบสุ่มบนแถบ ผู้เล่นคุม "แถบจับ" ด้วย กด OK ค้าง = เลื่อน
// เข้าหา, ปล่อย = ไหลกลับด้วยแรงโน้มถ่วง progress เพิ่มเมื่อแถบจับทับตำแหน่งปลา ลดเมื่อไม่ทับ (ช้ากว่าตอน
// เพิ่ม - ให้อภัยมากกว่า) เต็ม 100 = จับได้ ถ้า progress ค้างที่ 0 ต่อเนื่องเกิน grace period = ปลาหลุด
// (กันเล่นแย่มากๆแล้วไม่จบเกมสักที) ความเร็ว/ความกระตุกของปลาสเกลตาม reelDifficulty ของชนิดที่สุ่มไว้
// **ไม่บอกชื่อปลาตรงนี้** (รอเฉลยตอน RESULT เท่านั้น) กันสปอยล์ก่อนจะรู้ด้วยซ้ำว่าจับได้จริงไหม
// ---------------------------------------------------------------------------
void drawFischReelFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Reeling...");
  display.setCursor(0, 20);
  display.print("Hold OK to reel");

  int barX = 10, barY = 40, barW = 108, barH = 10;
  display.drawRect(barX, barY, barW, barH, SSD1306_WHITE);

  int fishX = barX + (int)(fischFishPos * barW);
  display.fillRect(fishX - 2, barY - 4, 4, 4, SSD1306_WHITE); // ตำแหน่งปลา (เหนือแถบ ไม่ชนกับแถบจับ)

  int capLeft = barX + (int)((fischCapturePos - fischCaptureHalfWidth) * barW);
  int capW = (int)(fischCaptureHalfWidth * 2 * barW);
  display.fillRect(capLeft, barY, capW, barH, SSD1306_WHITE);

  display.drawRect(10, 54, 108, 6, SSD1306_WHITE);
  int pw = (int)(fischReelProgress / 100.0f * 106);
  display.fillRect(11, 55, pw, 4, SSD1306_WHITE);
}

bool runFischReelPhase() {
  fischFishPos = 0.5f;
  fischFishVel = 0;
  fischCapturePos = 0.5f;
  fischCaptureVel = 0;
  fischReelProgress = 0;
  fischReelZeroSinceMs = 0;

  // ทุกค่าด้านล่างสเกลตาม reelDifficulty (0..100) ของปลาตัวนี้โดยเฉพาะ - นี่แหละคือจุดที่ทำให้แต่ละชนิด
  // "พิเศษ" กว่ากันจริงๆ ไม่ใช่แค่สุ่มเฉยๆ: ปลายากยิ่งขยับเร็ว/กระตุกแรงกว่า, แถบจับแคบกว่า, ตอนทับแล้ว
  // progress ขึ้นช้ากว่า, แต่พอหลุดร่วงเร็วกว่า - รวมกันแล้วปลา rarity สูงเล่นยากขึ้นจริงในทุกมิติพร้อมกัน
  int diff = FISCH_CATALOG[fischCurrentFishIndex].reelDifficulty; // 0..100 (ในคลังจริงอยู่ราว 15..95)

  fischCaptureHalfWidth = 0.20f - (diff / 100.0f) * 0.14f; // ง่าย 0.20 (กว้าง) -> ยาก 0.06 (แคบมาก)
  if (fischCaptureHalfWidth < 0.05f) fischCaptureHalfWidth = 0.05f;

  float rateIn = 34.0f - (diff / 100.0f) * 18.0f;   // ง่าย +34/วิ ตอนทับ -> ยาก +16/วิ (ขึ้นช้ากว่ามาก)
  float rateOut = 10.0f + (diff / 100.0f) * 22.0f;  // ง่าย -10/วิ ตอนหลุด -> ยาก -32/วิ (ร่วงเร็วกว่ามาก)

  float fishSpeed = 0.10f + (diff / 100.0f) * 0.55f; // ง่าย ~0.10 -> ยาก ~0.65 (กว้างกว่าเดิมชัดเจน)
  unsigned long fishRetargetIntervalMs = 650 - (diff * 5UL); // ยากยิ่งเปลี่ยนทิศถี่ขึ้น
  if (fishRetargetIntervalMs < 120) fishRetargetIntervalMs = 120;
  int jerkChancePercent = (diff * 40) / 100; // โอกาส "กระตุกแรง" ต่อรอบเปลี่ยนทิศ สูงสุด 40% ตอน diff=100

  fischTransitionTo(drawFischReelFrame);
  while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;

  unsigned long lastTick = millis();
  unsigned long lastRetarget = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      if (fischConfirmOverlay("Quit fishing?")) {
        fischQuitToMenu = true;
        return false;
      }
      drawFischReelFrame();
      display.display();
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }

    unsigned long now = millis();
    if (now - lastRetarget >= fishRetargetIntervalMs) {
      lastRetarget = now;
      bool jerk = random(0, 100) < jerkChancePercent; // ปลายากมีโอกาสพุ่งแรงกว่าปกติเป็นครั้งคราว
      float mag = jerk ? fishSpeed * 1.8f : fishSpeed;
      fischFishVel = ((random(0, 200) / 100.0f) - 1.0f) * mag; // -mag..+mag
    }

    if (now - lastTick >= FISCH_REEL_TICK_MS) {
      float dt = (now - lastTick) / 1000.0f;
      lastTick = now;

      // ปลา: เด้งแบบมี damping ตอนชนขอบถ้าเปิด Momentum ไว้ (Settings > Reel Momentum) ปิดไว้ = เด้งเต็ม
      // แรงแบบเดิมก่อนหน้านี้ (fischMomentumEnabled=false ทำให้ damping กลายเป็น 1.0 คือไม่หน่วงเลย)
      float bounceDamping = fischMomentumEnabled ? FISCH_BOUNCE_DAMPING : 1.0f;
      fischFishPos += fischFishVel * dt;
      if (fischFishPos < 0) { fischFishPos = 0; fischFishVel = -fischFishVel * bounceDamping; }
      if (fischFishPos > 1) { fischFishPos = 1; fischFishVel = -fischFishVel * bounceDamping; }

      // แถบจับ: เปิด Momentum = ใช้ความเร่ง+แรงโน้มถ่วงจริง (กด OK ค้าง = เร่งขึ้น, ปล่อย = ดึงกลับ, ชนขอบ
      // เด้งแบบหน่วงแรง) ปิด Momentum = ขยับตำแหน่งตรงๆทุก tick แบบดั้งเดิม (ไม่มีความเฉื่อย/เด้งเลย)
      bool holding = btnOk.isPressed();
      if (fischMomentumEnabled) {
        fischCaptureVel += (holding ? FISCH_CAPTURE_ACCEL : -FISCH_CAPTURE_GRAVITY) * dt;
        if (fischCaptureVel > FISCH_CAPTURE_MAX_SPEED) fischCaptureVel = FISCH_CAPTURE_MAX_SPEED;
        if (fischCaptureVel < -FISCH_CAPTURE_MAX_SPEED) fischCaptureVel = -FISCH_CAPTURE_MAX_SPEED;
        fischCapturePos += fischCaptureVel * dt;
        if (fischCapturePos < 0) { fischCapturePos = 0; fischCaptureVel = -fischCaptureVel * FISCH_BOUNCE_DAMPING; }
        if (fischCapturePos > 1) { fischCapturePos = 1; fischCaptureVel = -fischCaptureVel * FISCH_BOUNCE_DAMPING; }
      } else {
        fischCapturePos += (holding ? 0.9f : -0.7f) * dt;
        if (fischCapturePos < 0) fischCapturePos = 0;
        if (fischCapturePos > 1) fischCapturePos = 1;
        fischCaptureVel = 0;
      }

      bool overlap = fabs(fischCapturePos - fischFishPos) <= fischCaptureHalfWidth;
      if (overlap) {
        fischReelProgress += rateIn * dt;
        fischReelZeroSinceMs = 0;
      } else {
        fischReelProgress -= rateOut * dt;
        if (fischReelProgress <= 0) {
          fischReelProgress = 0;
          if (fischReelZeroSinceMs == 0) fischReelZeroSinceMs = now;
        }
      }
      if (fischReelProgress > 100) fischReelProgress = 100;

      drawFischReelFrame();
      display.display();

      if (fischReelProgress >= 100) {
        playBeepPattern(2, 50, 50);
        return true;
      }
      if (fischReelZeroSinceMs != 0 && (now - fischReelZeroSinceMs) >= FISCH_REEL_ESCAPE_GRACE_MS) {
        return false;
      }
    }
    delay(5);
  }
}

// ---------------------------------------------------------------------------
// Result / Escape - จบการตกปลาหนึ่งรอบ
// ---------------------------------------------------------------------------
void drawFischResultFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Caught!");
  display.setCursor(0, 16);
  display.println(FISCH_CATALOG[fischCurrentFishIndex].name);
  display.setCursor(0, 28);
  display.print(fischCaughtWeight, 2);
  display.print(" kg");

  if (fischCaughtIsNewSpecies) {
    display.setCursor(0, 40);
    display.print("NEW SPECIES!");
  } else if (fischCaughtIsNewBest) {
    display.setCursor(0, 40);
    display.print("NEW BEST!");
  }

  display.setCursor(0, 56);
  display.print("OK = continue");
}

void drawFischEscapeFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 24);
  display.print("The fish got away...");
}

/// เชื่อมทั้ง 4 เฟส (Cast->Bite->Hook->Reel) เข้าด้วยกัน เช็ค fischQuitToMenu หลังเรียกทุกเฟสเพื่อตัดออก
/// กลางคันทันทีถ้าผู้เล่นยืนยัน "Quit fishing?" ระหว่างทาง - เรียกจาก runFischMainMenu (fischMenuIndex==0)
void runFischFishingSequence() {
  fischQuitToMenu = false;

  int quality = runFischCastPhase();
  if (fischQuitToMenu) return;

  runFischBiteWaitPhase(quality);
  if (fischQuitToMenu) return;

  bool hooked = runFischHookReactionPhase();
  if (fischQuitToMenu) return;
  if (!hooked) {
    fischCache.totalEscapes++;
    fischSaveSaveData();
    Serial.println("[Fisch] ปลาหลุด (พลาดจังหวะกัด)");
    fischTransitionTo(drawFischEscapeFrame);
    delay(1400);
    return;
  }

  bool caught = runFischReelPhase();
  if (fischQuitToMenu) return;
  if (!caught) {
    fischCache.totalEscapes++;
    fischSaveSaveData();
    Serial.println("[Fisch] ปลาหลุด (reel แพ้)");
    fischTransitionTo(drawFischEscapeFrame);
    delay(1400);
    return;
  }

  const FischFishEntry& f = FISCH_CATALOG[fischCurrentFishIndex];
  float range = f.maxWeightKg - f.minWeightKg;
  fischCaughtWeight = f.minWeightKg + (random(0, 10001) / 10000.0f) * range;

  fischCaughtIsNewSpecies = (fischCache.bestWeightKg[fischCurrentFishIndex] <= 0.0f);
  fischCaughtIsNewBest = fischCaughtIsNewSpecies || (fischCaughtWeight > fischCache.bestWeightKg[fischCurrentFishIndex]);
  if (fischCaughtIsNewBest) fischCache.bestWeightKg[fischCurrentFishIndex] = fischCaughtWeight;
  fischCache.heldCount[fischCurrentFishIndex]++;
  fischCache.totalCatches++;
  fischSaveSaveData();

  Serial.print("[Fisch] จับได้: ");
  Serial.print(f.name);
  Serial.print(" ");
  Serial.print(fischCaughtWeight);
  Serial.println(" kg");

  playBeepPattern(fischCaughtIsNewSpecies ? 4 : 3, 60, 60);

  fischTransitionTo(drawFischResultFrame);
  while (btnOk.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  while (!btnOk.consumeJustPressed()) { delay(20); }
}

/// รองรับ "กดค้างเลื่อนเร็ว" สำหรับ Next/Prev ใน Fish Log และ Inventory (catalog ใหญ่ถึง 120 รายการ กด
/// ทีละครั้งไม่ไหว) - กดครั้งแรกขยับทันที 1 ก้าวเหมือนเดิม ถ้ายังกดค้างต่อเกิน FISCH_HOLD_REPEAT_DELAY_MS
/// จะเริ่ม auto-repeat ทุก FISCH_HOLD_REPEAT_INTERVAL_MS จนกว่าจะปล่อย holdStartMs/lastRepeatMs เป็น
/// state ที่ผู้เรียกต้องประกาศเป็น local เก็บข้าม loop iteration เอง (ฟังก์ชันนี้แค่แก้ค่าผ่าน reference)
bool fischHoldRepeatStep(Button& btn, unsigned long& holdStartMs, unsigned long& lastRepeatMs) {
  if (btn.consumeJustPressed()) {
    holdStartMs = millis();
    lastRepeatMs = holdStartMs;
    return true;
  }
  if (holdStartMs != 0 && btn.isPressed()) {
    unsigned long now = millis();
    if (now - holdStartMs >= FISCH_HOLD_REPEAT_DELAY_MS && now - lastRepeatMs >= FISCH_HOLD_REPEAT_INTERVAL_MS) {
      lastRepeatMs = now;
      return true;
    }
  }
  if (!btn.isPressed()) holdStartMs = 0;
  return false;
}

// ---------------------------------------------------------------------------
// Fish Log - "fishdex" ไล่ดูปลาทั้ง catalog ทีละตัวด้วย < / > (กดค้างเลื่อนเร็วได้ - ดู
// fischHoldRepeatStep) ชนิดที่ยังไม่เคยจับโชว์ "???" (ไม่ spoiler ชื่อ/rarity) ชนิดที่เคยจับโชว์ชื่อ +
// rarity + ตัวใหญ่สุดที่เคยได้ (ถาวร ไม่ลดแม้ heldCount จะเป็น 0 แล้วก็ตาม - แยกจาก Inventory ที่โชว์ของ
// ที่ถืออยู่ตอนนี้) ชื่อยาวเกินจอตัดด้วย fischTruncateToFit กันขึ้นบรรทัดใหม่ไปทับ layout
// ---------------------------------------------------------------------------
void drawFischLogFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Fish Log ");
  display.print(fischLogIndex + 1);
  display.print("/");
  display.print(FISCH_CATALOG_COUNT);

  bool discovered = fischCache.bestWeightKg[fischLogIndex] > 0.0f;
  display.setCursor(0, 16);
  display.print(discovered ? fischTruncateToFit(FISCH_CATALOG[fischLogIndex].name, SCREEN_WIDTH) : String("???"));

  display.setCursor(0, 28);
  display.print("Rarity: ");
  display.print(discovered ? FISCH_RARITY_NAMES[FISCH_CATALOG[fischLogIndex].rarity] : "???");

  if (discovered) {
    display.setCursor(0, 40);
    display.print("Best: ");
    display.print(fischCache.bestWeightKg[fischLogIndex], 2);
    display.print(" kg");
  }

  display.setCursor(0, 56);
  display.print("<> browse BACK exit");
}

void runFischLogMenu() {
  fischLogIndex = 0;
  fischTransitionTo(drawFischLogFrame);
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischLogIndex = (fischLogIndex + 1) % FISCH_CATALOG_COUNT;
      drawFischLogFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      fischLogIndex = (fischLogIndex - 1 + FISCH_CATALOG_COUNT) % FISCH_CATALOG_COUNT;
      drawFischLogFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Inventory - ปลาที่ถืออยู่ตอนนี้จริงๆ (heldCount > 0) read-only ไปก่อน รอ Shop ในอนาคตถึงจะขายลดจำนวนได้
// **แสดงเฉพาะชนิดที่เคยจับแล้วเท่านั้น** ไม่ใช่ทุกชนิดใน catalog แบบ Fish Log (ที่ตั้งใจโชว์ "???" ของ
// ที่ยังไม่เจอไว้เป็นเป้าหมายให้ตามหา) - เพราะ Inventory ไม่มีช่อง "???" กั้นสปอยล์แบบนั้น ถ้าเอา catalog
// ทั้งก้อนมาวนเหมือน Fish Log จะเห็นชื่อ/rarity ปลาที่ยังไม่เคยจับไปเลยทันที เลยต้องกรองก่อนแทน เลข
// นับ (n/count) จึงเปลี่ยนตามจำนวนชนิดที่จับได้จริง ไม่ใช่ FISCH_CATALOG_COUNT ตายตัว
// ---------------------------------------------------------------------------
int fischCountOwnedSpecies() {
  int count = 0;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    if (fischCache.heldCount[i] > 0) count++;
  }
  return count;
}

/// หา index จริงใน FISCH_CATALOG ของ "ชนิดที่ถืออยู่ตัวที่ ownedIdx" (นับเฉพาะ heldCount > 0) คืน -1 ถ้า
/// ownedIdx เกินจำนวนที่ถืออยู่จริง (ไม่ควรเกิดถ้าผู้เรียก mod ด้วย fischCountOwnedSpecies() ไว้ก่อนแล้ว)
int fischOwnedCatalogIndex(int ownedIdx) {
  int count = 0;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    if (fischCache.heldCount[i] > 0) {
      if (count == ownedIdx) return i;
      count++;
    }
  }
  return -1;
}

void drawFischInventoryFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  int ownedCount = fischCountOwnedSpecies();
  if (ownedCount == 0) {
    fischPrintCentered("Inventory empty", 24);
    fischPrintCentered("Go catch something!", 36);
    fischPrintCentered("BACK exit", 56);
    return;
  }

  int catIdx = fischOwnedCatalogIndex(fischInvIndex);

  display.setCursor(0, 0);
  display.print("Inventory ");
  display.print(fischInvIndex + 1);
  display.print("/");
  display.print(ownedCount);

  display.setCursor(0, 16);
  display.print(fischTruncateToFit(FISCH_CATALOG[catIdx].name, SCREEN_WIDTH));
  display.setCursor(0, 28);
  display.print("Rarity: ");
  display.print(FISCH_RARITY_NAMES[FISCH_CATALOG[catIdx].rarity]);
  display.setCursor(0, 40);
  display.print("Held: ");
  display.print(fischCache.heldCount[catIdx]);

  display.setCursor(0, 56);
  display.print("<> browse BACK exit");
}

void runFischInventoryMenu() {
  fischInvIndex = 0;
  fischTransitionTo(drawFischInventoryFrame);
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    int ownedCount = fischCountOwnedSpecies();
    // เรียก fischHoldRepeatStep ทุกรอบเสมอ (กิน justPressed edge ให้เรียบร้อยแม้ ownedCount==0) แล้วค่อย
    // ตัดสินใจว่าจะขยับ index จริงไหมทีหลัง - กัน state ของปุ่มค้างผิดจังหวะตอนไม่มีอะไรให้เลื่อนดู
    bool stepNext = fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat);
    bool stepPrev = fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat);
    if (ownedCount > 0) {
      if (stepNext) {
        fischInvIndex = (fischInvIndex + 1) % ownedCount;
        drawFischInventoryFrame();
        display.display();
      }
      if (stepPrev) {
        fischInvIndex = (fischInvIndex - 1 + ownedCount) % ownedCount;
        drawFischInventoryFrame();
        display.display();
      }
    }
    if (btnBack.consumeJustPressed()) return;
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Stats - หน้าเดียว โชว์ตัวเลขรวม ไม่มีอะไรให้กดนอกจาก BACK ออก
// ---------------------------------------------------------------------------
void drawFischStatsFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Fisch Stats");

  display.setCursor(0, 14);
  display.print("Casts: ");
  display.print(fischCache.totalCasts);

  display.setCursor(0, 26);
  display.print("Catches: ");
  display.print(fischCache.totalCatches);

  display.setCursor(0, 38);
  unsigned long attempts = fischCache.totalCatches + fischCache.totalEscapes;
  int rate = attempts > 0 ? (int)(100UL * fischCache.totalCatches / attempts) : 0;
  display.print("Success: ");
  display.print(rate);
  display.print("%");

  display.setCursor(0, 50);
  int bestIdx = fischFindBestCatchIndex();
  display.print("Best: ");
  display.print(bestIdx >= 0 ? FISCH_CATALOG[bestIdx].name : "-");
}

void runFischStatsPage() {
  fischTransitionTo(drawFischStatsFrame);
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  while (!btnBack.consumeJustPressed()) { delay(20); }
}

// ---------------------------------------------------------------------------
// Settings - เฉพาะของ Fisch เอง: toggle Reel Momentum (item 0, label เปลี่ยนตามสถานะแบบเดียวกับ
// Sound/Slot Animation ใน Others > Settings), ล้างเซฟทั้งหมด (item 1, มี confirm overlay กันกดพลาด),
// Back (item 2) - ปิด Momentum มีกวนๆ นิดหน่อยด้วย confirm ล้อเล่น (ดู runFischSettingsMenu) แต่เปิดกลับ
// ไม่ต้องถามอะไร ไม่มีอะไรเสียหาย
// ---------------------------------------------------------------------------
const char* const FISCH_SETTINGS_ITEMS[] = { "Reel Momentum", "Reset all data", "Back" };
const int FISCH_SETTINGS_ITEM_COUNT = 3;

void drawFischSettingsFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Fisch Settings");
  display.setCursor(0, 24);
  display.print("> ");
  if (fischSettingsIndex == 0) {
    display.print(fischMomentumEnabled ? "Reel Momentum: On" : "Reel Momentum: Off");
  } else {
    display.print(FISCH_SETTINGS_ITEMS[fischSettingsIndex]);
  }
  display.setCursor(0, 56);
  display.print("<> OK BACK");
}

void runFischSettingsMenu() {
  fischSettingsIndex = 0;
  fischTransitionTo(drawFischSettingsFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischSettingsIndex = (fischSettingsIndex + 1) % FISCH_SETTINGS_ITEM_COUNT;
      drawFischSettingsFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischSettingsIndex = (fischSettingsIndex - 1 + FISCH_SETTINGS_ITEM_COUNT) % FISCH_SETTINGS_ITEM_COUNT;
      drawFischSettingsFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      if (fischSettingsIndex == 0) {
        // เปิด -> ปิดตรงๆเลย ไม่มีอะไรเสียหาย แต่ปิด -> เปิด กวนๆนิดหน่อยด้วย confirm ล้อเล่น
        bool turningOff = fischMomentumEnabled;
        bool proceed = turningOff ? fischConfirmOverlay("Real ones don't.") : true;
        if (proceed) {
          fischMomentumEnabled = !fischMomentumEnabled;
          Preferences prefs;
          prefs.begin("fisch", false);
          prefs.putBool("momentumOn", fischMomentumEnabled);
          prefs.end();
          Serial.print("[Fisch] Reel Momentum ");
          Serial.println(fischMomentumEnabled ? "เปิดแล้ว" : "ปิดแล้ว");
        }
        drawFischSettingsFrame();
        display.display();
      } else if (fischSettingsIndex == 1) {
        if (fischConfirmOverlay("Reset all data?")) {
          memset(&fischCache, 0, sizeof(FischSaveData));
          fischSaveSaveData();
          Serial.println("[Fisch] ล้างเซฟทั้งหมดแล้ว");
          playBeepPattern(2, 80, 80);
        }
        drawFischSettingsFrame();
        display.display();
      } else {
        return;
      }
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Entry point - เรียกจาก runOthersMenu() ใน .ino หลัก
// ---------------------------------------------------------------------------
void runFischGame() {
  Serial.println("[Fisch] เข้าเกม");
  fischLoadSaveData();
  runFischLoading();
  runFischMainMenu();
  // ออกจาก main menu (กด BACK) = จบ กลับไป runOthersMenu ตามปกติ - ไม่ต้อง transition เพิ่มเพราะ
  // runOthersMenu จะวาดเมนู Others ทับหน้าจอเองทันทีที่ควบคุมกลับไปถึง (ดู showOthersMenuScreen)
}

#endif // FISCH_GAME_H
