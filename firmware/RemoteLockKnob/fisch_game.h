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

#include "gif_fisch_loading.h" // FISCH_LOADING_FRAMES[]/_FRAME_COUNT/_FRAME_DELAY_MS - วาดจาก Pic Mode
                                // Editor (ภาพ "FischLoading_gif") แปลงมาให้อัตโนมัติ - ห้ามแก้มือ วาดใหม่
                                // แล้วแปลงซ้ำแทนถ้าอยากเปลี่ยน (ดูหน้า Loading ใน fisch_game.h)
#include "fisch_world_map.h"    // FISCH_WORLD_MAP_BITMAP[]/_SIZE - พื้นหลังแมพเดินทาง 500x500 แปลงจาก
                                // map.png ที่ผู้ใช้วาดเอง (ห้ามแก้มือเหมือนกัน วาดใหม่แล้วแปลงซ้ำแทน)

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
  int8_t exclusiveMapIndex; // -1 = ปลาทั่วไป หาได้ทุกเกาะ (ปลาเดิม 120 ตัวทั้งหมดเป็นค่านี้) >=0 = หาได้
                            // เฉพาะเกาะนั้นเท่านั้น (index ตรงกับ FISCH_MAPS) ดู fischPickFishForRarity()
};

const FischFishEntry FISCH_CATALOG[] = {
  // Common (40)
  {"Tilapia", FISCH_COMMON, 0.05f, 0.49f, 8, -1},
  {"Mackerel", FISCH_COMMON, 0.11f, 0.65f, 11, -1},
  {"Catfish", FISCH_COMMON, 0.16f, 0.80f, 9, -1},
  {"Snakehead", FISCH_COMMON, 0.22f, 0.96f, 13, -1},
  {"Mud Carp", FISCH_COMMON, 0.28f, 0.77f, 11, -1},
  {"Sardine", FISCH_COMMON, 0.33f, 0.92f, 10, -1},
  {"Anchovy", FISCH_COMMON, 0.39f, 1.08f, 13, -1},
  {"Silver Carp", FISCH_COMMON, 0.45f, 0.89f, 12, -1},
  {"Grass Carp", FISCH_COMMON, 0.50f, 1.04f, 15, -1},
  {"Bighead Carp", FISCH_COMMON, 0.56f, 1.20f, 14, -1},
  {"Common Carp", FISCH_COMMON, 0.62f, 1.35f, 12, -1},
  {"Crucian Carp", FISCH_COMMON, 0.67f, 1.17f, 16, -1},
  {"Roach", FISCH_COMMON, 0.73f, 1.32f, 14, -1},
  {"Bluegill", FISCH_COMMON, 0.79f, 1.48f, 18, -1},
  {"Sunfish", FISCH_COMMON, 0.84f, 1.29f, 16, -1},
  {"Perch", FISCH_COMMON, 0.90f, 1.44f, 14, -1},
  {"Bream", FISCH_COMMON, 0.96f, 1.60f, 18, -1},
  {"Rudd", FISCH_COMMON, 1.01f, 1.75f, 16, -1},
  {"Minnow", FISCH_COMMON, 1.07f, 1.56f, 20, -1},
  {"Goldfish", FISCH_COMMON, 1.13f, 1.72f, 18, -1},
  {"Guppy", FISCH_COMMON, 1.18f, 1.87f, 17, -1},
  {"Molly", FISCH_COMMON, 1.24f, 1.68f, 20, -1},
  {"Herring", FISCH_COMMON, 1.30f, 1.84f, 19, -1},
  {"Smelt", FISCH_COMMON, 1.35f, 1.99f, 22, -1},
  {"Flounder", FISCH_COMMON, 1.41f, 2.15f, 21, -1},
  {"Sole", FISCH_COMMON, 1.47f, 1.96f, 19, -1},
  {"Whiting", FISCH_COMMON, 1.53f, 2.12f, 23, -1},
  {"Pollock", FISCH_COMMON, 1.58f, 2.27f, 21, -1},
  {"Haddock", FISCH_COMMON, 1.64f, 2.08f, 24, -1},
  {"Cod", FISCH_COMMON, 1.70f, 2.24f, 23, -1},
  {"Pilchard", FISCH_COMMON, 1.75f, 2.39f, 21, -1},
  {"Gudgeon", FISCH_COMMON, 1.81f, 2.55f, 25, -1},
  {"Loach", FISCH_COMMON, 1.87f, 2.36f, 23, -1},
  {"Climbing Perch", FISCH_COMMON, 1.92f, 2.51f, 27, -1},
  {"Walking Catfish", FISCH_COMMON, 1.98f, 2.67f, 25, -1},
  {"Java Barb", FISCH_COMMON, 2.04f, 2.48f, 24, -1},
  {"Silver Barb", FISCH_COMMON, 2.09f, 2.63f, 27, -1},
  {"Tinfoil Barb", FISCH_COMMON, 2.15f, 2.79f, 26, -1},
  {"Featherback", FISCH_COMMON, 2.21f, 2.94f, 29, -1},
  {"Glass Fish", FISCH_COMMON, 2.26f, 2.75f, 28, -1},
  // Uncommon (32)
  {"Seabass", FISCH_UNCOMMON, 0.20f, 1.37f, 27, -1},
  {"Squid", FISCH_UNCOMMON, 0.39f, 1.82f, 30, -1},
  {"Eel", FISCH_UNCOMMON, 0.58f, 2.27f, 29, -1},
  {"Snakeskin Gourami", FISCH_UNCOMMON, 0.77f, 2.72f, 32, -1},
  {"Giant Gourami", FISCH_UNCOMMON, 0.95f, 2.25f, 31, -1},
  {"Kissing Gourami", FISCH_UNCOMMON, 1.14f, 2.70f, 29, -1},
  {"Pearl Gourami", FISCH_UNCOMMON, 1.33f, 3.15f, 33, -1},
  {"Red Tail Tinfoil", FISCH_UNCOMMON, 1.52f, 2.69f, 31, -1},
  {"Rohu", FISCH_UNCOMMON, 1.71f, 3.14f, 35, -1},
  {"Catla", FISCH_UNCOMMON, 1.90f, 3.59f, 33, -1},
  {"Mrigal", FISCH_UNCOMMON, 2.09f, 4.04f, 32, -1},
  {"Pangasius", FISCH_UNCOMMON, 2.28f, 3.58f, 35, -1},
  {"Iridescent Shark", FISCH_UNCOMMON, 2.46f, 4.02f, 34, -1},
  {"Juvenile Redtail", FISCH_UNCOMMON, 2.65f, 4.47f, 37, -1},
  {"Walleye", FISCH_UNCOMMON, 2.84f, 4.01f, 36, -1},
  {"Pike", FISCH_UNCOMMON, 3.03f, 4.46f, 34, -1},
  {"Muskellunge", FISCH_UNCOMMON, 3.22f, 4.91f, 38, -1},
  {"Zander", FISCH_UNCOMMON, 3.41f, 5.36f, 36, -1},
  {"Trout", FISCH_UNCOMMON, 3.60f, 4.90f, 40, -1},
  {"Char", FISCH_UNCOMMON, 3.79f, 5.35f, 38, -1},
  {"Grayling", FISCH_UNCOMMON, 3.97f, 5.79f, 37, -1},
  {"Whitefish", FISCH_UNCOMMON, 4.16f, 5.33f, 40, -1},
  {"Mullet", FISCH_UNCOMMON, 4.35f, 5.78f, 39, -1},
  {"Garfish", FISCH_UNCOMMON, 4.54f, 6.23f, 42, -1},
  {"Needlefish", FISCH_UNCOMMON, 4.73f, 6.68f, 41, -1},
  {"Halfbeak", FISCH_UNCOMMON, 4.92f, 6.22f, 39, -1},
  {"Triggerfish", FISCH_UNCOMMON, 5.11f, 6.67f, 43, -1},
  {"Parrotfish", FISCH_UNCOMMON, 5.30f, 7.12f, 41, -1},
  {"Snapper", FISCH_UNCOMMON, 5.48f, 6.65f, 45, -1},
  {"Small Grouper", FISCH_UNCOMMON, 5.67f, 7.10f, 43, -1},
  {"Small Barracuda", FISCH_UNCOMMON, 5.86f, 7.55f, 42, -1},
  {"Mahi-mahi", FISCH_UNCOMMON, 6.05f, 8.00f, 46, -1},
  // Rare (24)
  {"Koi Carp", FISCH_RARE, 1.00f, 4.60f, 44, -1},
  {"Stingray", FISCH_RARE, 1.78f, 6.18f, 47, -1},
  {"Electric Eel", FISCH_RARE, 2.57f, 7.77f, 46, -1},
  {"Sturgeon", FISCH_RARE, 3.35f, 9.35f, 50, -1},
  {"Paddlefish", FISCH_RARE, 4.13f, 8.13f, 48, -1},
  {"Gar", FISCH_RARE, 4.91f, 9.71f, 47, -1},
  {"Alligator Gar", FISCH_RARE, 5.70f, 11.30f, 51, -1},
  {"Wels Catfish", FISCH_RARE, 6.48f, 10.08f, 49, -1},
  {"Arapaima", FISCH_RARE, 7.26f, 11.66f, 53, -1},
  {"Arowana", FISCH_RARE, 8.04f, 13.24f, 52, -1},
  {"Peacock Bass", FISCH_RARE, 8.83f, 14.83f, 50, -1},
  {"Tarpon", FISCH_RARE, 9.61f, 13.61f, 54, -1},
  {"Bonefish", FISCH_RARE, 10.39f, 15.19f, 53, -1},
  {"Permit", FISCH_RARE, 11.17f, 16.77f, 57, -1},
  {"Giant Trevally", FISCH_RARE, 11.96f, 15.56f, 55, -1},
  {"Yellowfin Tuna", FISCH_RARE, 12.74f, 17.14f, 54, -1},
  {"Bluefin Tuna", FISCH_RARE, 13.52f, 18.72f, 58, -1},
  {"Swordfish", FISCH_RARE, 14.30f, 20.30f, 56, -1},
  {"Marlin", FISCH_RARE, 15.09f, 19.09f, 60, -1},
  {"Sailfish", FISCH_RARE, 15.87f, 20.67f, 59, -1},
  {"Blacktip Shark", FISCH_RARE, 16.65f, 22.25f, 57, -1},
  {"Hammerhead Shark", FISCH_RARE, 17.43f, 21.03f, 61, -1},
  {"Manta Ray", FISCH_RARE, 18.22f, 22.62f, 60, -1},
  {"Goliath Grouper", FISCH_RARE, 19.00f, 24.20f, 64, -1},
  // Epic (16)
  {"Giant Snakehead", FISCH_EPIC, 5.00f, 16.25f, 61, -1},
  {"Giant Barb", FISCH_EPIC, 8.75f, 22.50f, 65, -1},
  {"Giant Stingray", FISCH_EPIC, 12.50f, 28.75f, 64, -1},
  {"Redtail Catfish", FISCH_EPIC, 16.25f, 35.00f, 68, -1},
  {"Piraiba Catfish", FISCH_EPIC, 20.00f, 32.50f, 67, -1},
  {"Chinese Paddlefish", FISCH_EPIC, 23.75f, 38.75f, 66, -1},
  {"Beluga Sturgeon", FISCH_EPIC, 27.50f, 45.00f, 70, -1},
  {"Great White Shark", FISCH_EPIC, 31.25f, 42.50f, 69, -1},
  {"Whale Shark", FISCH_EPIC, 35.00f, 48.75f, 74, -1},
  {"Basking Shark", FISCH_EPIC, 38.75f, 55.00f, 73, -1},
  {"Colossal Squid", FISCH_EPIC, 42.50f, 61.25f, 72, -1},
  {"Giant Octopus", FISCH_EPIC, 46.25f, 58.75f, 76, -1},
  {"Giant Moray Eel", FISCH_EPIC, 50.00f, 65.00f, 75, -1},
  {"Ocean Sunfish", FISCH_EPIC, 53.75f, 71.25f, 79, -1},
  {"Frilled Shark", FISCH_EPIC, 57.50f, 68.75f, 78, -1},
  {"Six-Gill Shark", FISCH_EPIC, 61.25f, 75.00f, 78, -1},
  // Legendary (8)
  {"Mekong Catfish", FISCH_LEGENDARY, 15.00f, 42.75f, 79, -1},
  {"Oarfish", FISCH_LEGENDARY, 34.82f, 68.74f, 84, -1},
  {"Coelacanth", FISCH_LEGENDARY, 54.64f, 94.73f, 84, -1},
  {"Megamouth Shark", FISCH_LEGENDARY, 74.46f, 120.71f, 90, -1},
  {"Giant Squid", FISCH_LEGENDARY, 94.29f, 125.12f, 90, -1},
  {"White Sturgeon", FISCH_LEGENDARY, 114.11f, 151.11f, 91, -1},
  {"Giant Whipray", FISCH_LEGENDARY, 133.93f, 177.10f, 96, -1},
  {"Greenland Shark", FISCH_LEGENDARY, 153.75f, 181.50f, 97, -1},

  // ปลาประจำเกาะ (exclusive) - หาได้เฉพาะที่เกาะนั้นเท่านั้น (ดู exclusiveMapIndex/fischPickFishForRarity)
  // เกาะละ 8 ตัว ผสม rarity ไล่ตามความยากของเกาะ (เกาะไกล/level สูง = สัดส่วนของหายากเยอะขึ้น) index ต้อง
  // ตรงกับลำดับจริงใน FISCH_MAPS ด้านล่างเป๊ะๆ (0=Home Dock, 1=Scatter Shoals, ... 10=Farhaven)
  // Home Dock (0)
  {"Dock Minnow", FISCH_COMMON, 0.05f, 0.40f, 7, 0},
  {"Puddle Perch", FISCH_COMMON, 0.10f, 0.50f, 9, 0},
  {"Rookie Bass", FISCH_COMMON, 0.15f, 0.60f, 10, 0},
  {"First Catch Carp", FISCH_COMMON, 0.20f, 0.70f, 11, 0},
  {"Morning Snapper", FISCH_UNCOMMON, 0.50f, 2.00f, 28, 0},
  {"Dockside Eel", FISCH_UNCOMMON, 0.80f, 2.50f, 30, 0},
  {"Beginner's Luck Trout", FISCH_UNCOMMON, 1.00f, 3.00f, 29, 0},
  {"Lucky Dock Koi", FISCH_RARE, 2.00f, 6.00f, 46, 0},
  // Scatter Shoals (1)
  {"Shoal Sardine", FISCH_COMMON, 0.10f, 0.60f, 10, 1},
  {"Scatter Anchovy", FISCH_COMMON, 0.15f, 0.70f, 12, 1},
  {"Drift Herring", FISCH_COMMON, 0.20f, 0.80f, 13, 1},
  {"Swarm Mackerel", FISCH_UNCOMMON, 0.60f, 2.20f, 29, 1},
  {"Cluster Snapper", FISCH_UNCOMMON, 0.80f, 2.50f, 31, 1},
  {"Ripple Chub", FISCH_UNCOMMON, 1.00f, 2.80f, 30, 1},
  {"Vortex Barracuda", FISCH_RARE, 3.00f, 9.00f, 48, 1},
  {"Whirl Jack", FISCH_RARE, 3.50f, 10.00f, 50, 1},
  // Reef Point (2)
  {"Reef Damsel", FISCH_COMMON, 0.10f, 0.50f, 12, 2},
  {"Coral Wrasse", FISCH_COMMON, 0.20f, 0.70f, 14, 2},
  {"Reef Butterflyfish", FISCH_UNCOMMON, 0.50f, 2.00f, 32, 2},
  {"Tidewave Angelfish", FISCH_UNCOMMON, 0.80f, 2.50f, 34, 2},
  {"Cresting Surgeonfish", FISCH_UNCOMMON, 1.00f, 2.80f, 33, 2},
  {"Reef Lionfish", FISCH_RARE, 3.00f, 10.00f, 51, 2},
  {"Wavecrest Grouper", FISCH_RARE, 4.00f, 12.00f, 53, 2},
  {"Point Break Ray", FISCH_EPIC, 10.00f, 30.00f, 63, 2},
  // Tide Hollow (3)
  {"Hollow Goby", FISCH_COMMON, 0.10f, 0.50f, 14, 3},
  {"Tidepool Blenny", FISCH_COMMON, 0.20f, 0.80f, 15, 3},
  {"Hollow Flounder", FISCH_UNCOMMON, 0.80f, 2.50f, 35, 3},
  {"Ebb Snapper", FISCH_UNCOMMON, 1.00f, 3.00f, 36, 3},
  {"Tide Turner Eel", FISCH_RARE, 3.50f, 11.00f, 54, 3},
  {"Hollow Conger", FISCH_RARE, 4.00f, 12.00f, 55, 3},
  {"Undertow Skate", FISCH_RARE, 4.50f, 13.00f, 56, 3},
  {"Hollow Leviathan Pup", FISCH_EPIC, 12.00f, 32.00f, 65, 3},
  // Kettle Cove (4)
  {"Kettle Guppy", FISCH_COMMON, 0.15f, 0.60f, 16, 4},
  {"Simmer Snapper", FISCH_UNCOMMON, 1.00f, 3.00f, 37, 4},
  {"Steamvent Chub", FISCH_UNCOMMON, 1.20f, 3.20f, 38, 4},
  {"Kettle Cod", FISCH_RARE, 4.00f, 13.00f, 57, 4},
  {"Boilspring Bass", FISCH_RARE, 4.50f, 14.00f, 58, 4},
  {"Cauldron Catfish", FISCH_RARE, 5.00f, 15.00f, 59, 4},
  {"Kettle Kraken Pup", FISCH_EPIC, 14.00f, 36.00f, 68, 4},
  {"Steamscale Ray", FISCH_EPIC, 15.00f, 38.00f, 69, 4},
  // Bean Shoal (5)
  {"Bean Sprout Minnow", FISCH_COMMON, 0.20f, 0.70f, 17, 5},
  {"Legume Snapper", FISCH_UNCOMMON, 1.20f, 3.50f, 39, 5},
  {"Podfish", FISCH_UNCOMMON, 1.50f, 3.80f, 40, 5},
  {"Bean Pod Barracuda", FISCH_RARE, 5.00f, 16.00f, 60, 5},
  {"String Bean Eel", FISCH_RARE, 5.50f, 17.00f, 61, 5},
  {"Kidney Bean Ray", FISCH_EPIC, 16.00f, 40.00f, 70, 5},
  {"Legume Leviathan", FISCH_EPIC, 18.00f, 42.00f, 71, 5},
  {"Great Bean Whale", FISCH_LEGENDARY, 20.00f, 60.00f, 81, 5},
  // Ring Islet (6)
  {"Ring Wrasse", FISCH_UNCOMMON, 1.50f, 4.00f, 41, 6},
  {"Halo Chub", FISCH_UNCOMMON, 1.80f, 4.20f, 42, 6},
  {"Ring Barracuda", FISCH_RARE, 6.00f, 18.00f, 62, 6},
  {"Loop Grouper", FISCH_RARE, 6.50f, 19.00f, 63, 6},
  {"Ring Leviathan", FISCH_EPIC, 18.00f, 45.00f, 72, 6},
  {"Halo Ray", FISCH_EPIC, 20.00f, 48.00f, 73, 6},
  {"Donut Shark", FISCH_EPIC, 22.00f, 50.00f, 74, 6},
  {"Ring Wyrm", FISCH_LEGENDARY, 25.00f, 70.00f, 83, 6},
  // Squirrel Isle (7)
  {"Acorn Perch", FISCH_UNCOMMON, 1.80f, 4.50f, 43, 7},
  {"Squirrelfish", FISCH_RARE, 6.50f, 19.00f, 64, 7},
  {"Nutcracker Eel", FISCH_RARE, 7.00f, 20.00f, 65, 7},
  {"Acorn Puffer", FISCH_EPIC, 22.00f, 50.00f, 75, 7},
  {"Bushytail Ray", FISCH_EPIC, 24.00f, 52.00f, 76, 7},
  {"Treehop Shark", FISCH_EPIC, 26.00f, 55.00f, 77, 7},
  {"Great Squirrel Whale", FISCH_LEGENDARY, 28.00f, 75.00f, 84, 7},
  {"Canopy Colossus", FISCH_LEGENDARY, 30.00f, 80.00f, 85, 7},
  // Sentinel Ring (8)
  {"Sentinel Snapper", FISCH_UNCOMMON, 2.00f, 5.00f, 44, 8},
  {"Vault Grouper", FISCH_RARE, 7.50f, 21.00f, 66, 8},
  {"Padlock Ray", FISCH_EPIC, 26.00f, 55.00f, 78, 8},
  {"Sentinel Shark", FISCH_EPIC, 28.00f, 58.00f, 79, 8},
  {"Warden Eel", FISCH_EPIC, 30.00f, 60.00f, 80, 8},
  {"Vault Leviathan", FISCH_LEGENDARY, 32.00f, 85.00f, 86, 8},
  {"Sentinel Wyrm", FISCH_LEGENDARY, 35.00f, 90.00f, 87, 8},
  {"Keykeeper Kraken", FISCH_LEGENDARY, 38.00f, 95.00f, 88, 8},
  // Grand Shoal (9)
  {"Grand Barracuda", FISCH_RARE, 8.00f, 22.00f, 67, 9},
  {"Majestic Grouper", FISCH_RARE, 8.50f, 23.00f, 68, 9},
  {"Grand Ray", FISCH_EPIC, 32.00f, 65.00f, 81, 9},
  {"Majestic Shark", FISCH_EPIC, 34.00f, 68.00f, 82, 9},
  {"Sovereign Eel", FISCH_EPIC, 36.00f, 70.00f, 83, 9},
  {"Grand Leviathan", FISCH_LEGENDARY, 40.00f, 100.00f, 89, 9},
  {"Majestic Wyrm", FISCH_LEGENDARY, 45.00f, 110.00f, 90, 9},
  {"Sovereign Kraken", FISCH_LEGENDARY, 50.00f, 120.00f, 91, 9},
  // Farhaven (10)
  {"Farhaven Barracuda", FISCH_RARE, 9.00f, 24.00f, 69, 10},
  {"Farhaven Ray", FISCH_EPIC, 38.00f, 72.00f, 84, 10},
  {"Abyssal Shark", FISCH_EPIC, 40.00f, 75.00f, 85, 10},
  {"Distant Eel", FISCH_EPIC, 42.00f, 78.00f, 86, 10},
  {"Farhaven Leviathan", FISCH_LEGENDARY, 55.00f, 130.00f, 92, 10},
  {"Abyssal Wyrm", FISCH_LEGENDARY, 60.00f, 140.00f, 93, 10},
  {"Horizon Kraken", FISCH_LEGENDARY, 65.00f, 150.00f, 94, 10},
  {"Farhaven Sovereign", FISCH_LEGENDARY, 70.00f, 181.50f, 97, 10},
};
const int FISCH_CATALOG_COUNT = sizeof(FISCH_CATALOG) / sizeof(FISCH_CATALOG[0]);

// ---------------------------------------------------------------------------
// Rarity -> ราคาขายปลา - ราคาฐาน (สะท้อนความหายาก) + ราคาต่อกิโล (สะท้อนขนาดตัวที่จับได้จริง) ทั้งคู่อิง
// tier เดียวกัน ดู fischSellPriceFor() - ลดลงจากเดิมมาเล็กน้อย (เดิม 5/15/40/100/300 + 1-5/kg) ตัวปรับ
// หลักของ "เงินได้ง่ายเกินไป" คือ fischRollRarity() ที่ทำให้เจอของหายากบ่อยเกิน (แก้ไปแล้วด้านล่าง) ราคา
// ตรงนี้แค่ลดเสริมอีกชั้นนึง ไม่ต้องหั่นหนักเท่าตอนแรกที่คิดไว้
const uint32_t FISCH_SELL_BASE_BY_TIER[] = { 4, 12, 30, 70, 180 };
const float FISCH_SELL_PER_KG_BY_TIER[] = { 0.8f, 1.5f, 2.5f, 3.5f, 4.5f };

// XP ต่อการจับได้ 1 ตัว อิงตาม rarity - ยิ่งหายากยิ่งได้เยอะ (สัดส่วนเดียวกับราคาขายคร่าวๆ)
const uint32_t FISCH_XP_BY_TIER[] = { 5, 15, 40, 100, 250 };

// โบนัส XP ตามเกาะที่กำลังตกปลาอยู่ - เกาะที่ level ต้องใช้สูง (levelRequired ของ FISCH_MAPS) ให้ XP เพิ่ม
// ตามสัดส่วน ชดเชยที่ level เกาะ/เบ็ด endgame ดันไปถึงหลักร้อย-พันแล้ว (ดู fischXpForLevel) ถ้าไม่มีโบนัสนี้
// แทบเป็นไปไม่ได้ที่จะไต่ไปถึงเกาะ/เบ็ดท้ายๆเลย - หาร 250 เพราะเกาะสุดท้าย (level 500) จะได้ตัวคูณ 3 เท่าพอดี
const float FISCH_ISLAND_XP_BONUS_DIVISOR = 250.0f;

// ---------------------------------------------------------------------------
// ตารางเบ็ด - เหมือน fish catalog เลย เพิ่มเบ็ดใหม่ทีหลังได้แค่เพิ่ม element ในนี้ ไม่ต้องแก้ logic ที่ไหน
// index 0 (Basic Rod) ถือว่า "มีติดตัวเสมอ" ไม่ต้องซื้อ ไม่ต้องเก็บสถานะเป็นเจ้าของแยก (ดู runFischRodMenu/
// runFischShopBuyRodMenu) เบ็ดอื่นต้องซื้อก่อนถึงจะใส่ได้ - ค่า bonus ติดลบได้ (เบ็ดขำๆ/glass cannon บาง
// อันแย่กว่า Basic Rod ในบางด้านจริงๆ) isRandom = true มีแค่ "Random Rod" ตัวเดียว ค่า bonus ในตารางไม่มี
// ผลอะไรกับมันเลย (สุ่มใหม่ทุกครั้งที่กด Go Fishing แทน - ดู fischResolveEquippedRodStats)
// ---------------------------------------------------------------------------
// สเตตเบ็ด - รีดีไซน์ให้ตรงกับ 5 สเตตหลักของ Fisch ตัวจริง (Lure Speed/Luck/Control/Resilience/Max Kg - ดู
// fischipedia.org/wiki/Fishing_Rods) เดิม reelEaseBonus ตัวเดียวไปลด "diff" กลางๆที่กระทบทั้งความเร็วปลา,
// ความกว้างแถบจับ, และอัตรา progress พร้อมกันหมด ทับซ้อนกับ captureWidthBonus เอง ตอนนี้แยกให้ชัดเจน:
// Resilience คุมแค่ความเร็ว/ความกระตุกของปลาอย่างเดียว, Control คุมแค่ความกว้างแถบจับอย่างเดียว (เหมือนเดิม
// แค่เปลี่ยนชื่อ), progressRateBonus คุมแค่อัตรา progress อย่างเดียว (เหมือนเดิม) - ไม่ทับซ้อนกันอีกต่อไป
// hookMsBonus/hookMissBonus เป็นสเตตของเราเอง (เกมจริงไม่มีเฟส Hook Reaction) ไม่ได้อิงจาก 5 สเตตนี้
struct FischRodEntry {
  const char* name;
  int levelRequired;      // level ที่ต้องถึงก่อนถึงจะซื้อได้ (เหมือน FischMapEntry.levelRequired) เบ็ดเดิม
                           // ทั้งหมดเป็น 1 (ซื้อได้ตั้งแต่ต้นเกม เหมือนก่อนเพิ่ม field นี้) มีผลจริงแค่เบ็ด
                           // ระดับสูงชุดใหม่ท้ายตาราง (Ancient Rod ขึ้นไป) ดู runFischRodBrowseMenu
  uint32_t cost;
  int resilienceBonus;    // ลด "moveDiff" ที่ใช้คำนวณความเร็ว/ความกระตุกของปลาเท่านั้น (ดู runFischReelPhase)
                           // ไม่แตะความกว้างแถบจับหรืออัตรา progress เลย (เดิมชื่อ reelEaseBonus)
  int luckBonus;           // บวกเข้า bonus ของ fischRollRarity ตรงๆ (Universal Luck - เดิมชื่อ luckyCastBonus)
  int hookMsBonus;        // บวกเข้า windowMs ต่อครั้งใน Hook reaction
  int hookMissBonus;      // บวกเข้า fischHookMissesAllowed
  int controlBonus;       // บวกเข้า fischCaptureHalfWidth ตรงๆ (หน่วย: 1 = 0.01) เดิมชื่อ captureWidthBonus
  int progressRateBonus;  // บวกเข้า rateIn (ความเร็ว progress ตอนแถบจับทับปลา) ตรงๆ - ค่าปกติของ rateIn
                           // อยู่แค่ราว 16-34 เท่านั้น เผื่อไว้ให้เบ็ดสุดขั้ว/ขำๆ เล่นกับมันได้ (ดู So
                           // Heavy Rod / Near Impossible Rod ด้านล่าง - ค่า ±900 คือของขำ ไม่ใช่ค่าปกติ)
  int lureSpeedBonus;      // ลดเวลารอกัดใน Bite phase (ดู runFischBiteWaitPhase) - สเตตใหม่ที่ไม่เคยมีมาก่อน
  float maxWeightKg;      // น้ำหนักปลาสูงสุดที่เบ็ดนี้รับไหว - ปลาที่จับได้หนักเกินนี้ = tension ขึ้นเร็ว
                           // กว่าปกติมากตลอดเวลา (ไม่ต้องกด OK ค้างก็ขึ้น ดู fischReelOverloaded ใน
                           // runFischReelPhase) 0 = ไม่จำกัด (ไม่มีเบ็ดไหนใช้ค่านี้ยกเว้นของขำสองอันท้ายตาราง)
  int wormholeChancePercent; // 0-100 โอกาสต่อครั้งที่จะจับปลาเฉพาะเกาะ "อื่น" (สุ่มเลือก ไม่ใช่เกาะที่อยู่
                           // ตอนนี้) แทนที่จะเป็นปลาทั่วไป/เกาะปัจจุบันตามปกติ - แทบทุกเบ็ดเป็น 0 มีผลจริงแค่
                           // Wanderer's Rod กับเบ็ด endgame 6 ตัวท้ายตาราง (ดู fischPickFishForRarity)
  bool isRandom;
};

// น้ำหนักสูงสุดที่แต่ละเบ็ดรับไหว (คอลัมน์สุดท้ายก่อน isRandom) - อิงจากช่วงน้ำหนักจริงในตาราง FISCH_CATALOG
// (Common สูงสุด ~2.9kg, Uncommon ~8kg, Rare ~24kg, Epic ~75kg, Legendary ~181.5kg) เบ็ดถูก/พื้นฐานรับได้แค่
// ปลาเล็ก อยากล่าปลาหนักๆต้องลงทุนซื้อเบ็ดแพงขึ้น (ดู fischReelOverloaded)
// ลำดับ field: name, levelRequired, cost, resilience, luck, hookMs, hookMiss, control, progressRate,
// lureSpeed, maxWeightKg, wormholeChancePercent, isRandom
const FischRodEntry FISCH_RODS[] = {
  {"Basic Rod",         1,    0,    0,   0,    0,   0,  0,   0,   0,   8, 0, false},
  // Cracked Rod/Rusty Rod เดิมด้อยกว่า Basic Rod (ฟรี) ทุกด้านทั้งที่ต้องเสียเงินซื้อ - ไม่มีเหตุผลให้ใครซื้อ
  // เลย ผู้ใช้ให้แก้: Cracked ได้ luck เล็กน้อยชดเชย (แตกแต่โชคดีแปลกๆ) เป็นตัวเลือกลัคราคาประหยัดก่อนเก็บเงิน
  // ซื้อ Keen Rod ($80) ส่วน Rusty ได้ max weight เพิ่มมาก (เก่าแต่ทนทาน) เป็นตัวเลือกลองจับปลาหนักราคาถูก
  {"Cracked Rod",       1,   20,  -5,   8,    0,   0,  0,   0,   0,   6, 0, false},
  {"Rusty Rod",         1,   30,   0,   0, -100,  -1,  0,   0,   0,  20, 0, false},
  {"Sturdy Rod",        1,    80,   8,   0,    0,   0,  0,   0,   0,  14, 0, false},
  {"Keen Rod",          1,    80,   0,   6,    0,   0,  0,   0,   0,  12, 0, false},
  {"Patient Rod",       1,    80,   0,   0,   60,   1,  0,   0,   0,  12, 0, false},
  {"Random Rod",        1,    50,   0,   0,    0,   0,  0,   0,   0,   0, 0, true}, // สุ่มทุกสเตต ดูช่วงสุ่มด้านล่าง
  {"Grandpa's Rod",     1,    60,  -5,  -5,  150,   0,  0,   0,   0,   9, 0, false},
  {"Heavy Rod",         1,   150,  15,  -8,    0,   0,  0,   0,   0,  30, 0, false},
  // Twitchy = ไวตอบสนอง (lure speed) แต่ทำให้ปลาดิ้นกระตุกมากขึ้นด้วย (resilience ติดลบ) ชดเชยด้วย luck สูง
  {"Twitchy Rod",       1,   150,  -5,  15,  -80,   0,  0,   0,  10,  18, 0, false},
  {"Featherlight Rod",  1,   300,   0,   0,  -40,   0,  0,   0,  18,  15, 0, false},
  {"Lucky Charm Rod",   1,   400, -10,  20,    0,   0,  0,   0,   0,  25, 0, false},
  {"Balanced Rod",      1,   250,   3,   4,   30,   0,  0,   0,   3,  30, 0, false}, // "สมดุล" แตะทุกสเตตนิดหน่อย
  {"Angler's Rod",      1,   500,  10,   6,    0,   0,  0,   0,   0,  45, 0, false},
  {"Prototype Rod",     1,   700,  25, -15,    0,   0,  0,   0,   0,  50, 0, false},
  {"Cursed Rod",        1,    10, -20, -20, -150,  -3,  0,   0,   0,   4, 0, false},
  {"Master Rod",        1,   900,  14,  12,   90,   2,  0,   0,   8, 100, 0, false},
  {"Legendary Rod",     1,  2000,  20,  18,  120,   3,  0,   0,  12, 200, 0, false}, // ครอบคลุมปลาหนักสุดในเกม (181.5kg)
  // 2 อันขำๆ ที่ผู้ใช้เสนอมา - เล่นกับ control สุดขั้ว + มีพฤติกรรมพิเศษเฉพาะตัวที่ special-case ไว้ตรงๆใน
  // runFischReelPhase เลย (เช็คด้วย FISCH_ROD_IDX_SO_HEAVY/_NEAR_IMPOSSIBLE) ไม่ได้ใช้สูตร rateIn/rateOut
  // ปกติเหมือนเบ็ดอื่น - progressRateBonus ของสองอันนี้เลยเหลือ 0 ไม่มีความหมายอะไรแล้ว - ต้องอยู่ index
  // 18/19 ตายตัว (ดู FISCH_ROD_IDX_SO_HEAVY/_NEAR_IMPOSSIBLE) เบ็ดชุดใหม่ที่แรงกว่าเลยต่อท้ายจากตรงนี้แทน
  // ที่จะแทรกก่อน Legendary Rod เพื่อไม่ต้องขยับ index ของสองอันนี้
  {"So Heavy Rod",      1,   100,   0,   0,    0,   0, 40,   0,   0, 999, 0, false}, // แถบจับเต็มบาร์แทบจับพลาดไม่ได้เลย
                                                                     // แต่ progress ขึ้นทีละนิดคงที่ตลอดเวลา
                                                                     // ไม่สนว่าทับปลาหรือเปล่า (~+1/2วิ) ไม่มี
                                                                     // ทางหลุดเพราะ progress ตก แต่ช้ามาก
  {"Near Impossible Rod",1,   100,   0,   0,    0,   0,-38,   0,   0, 999, 0, false}, // แถบจับเล็กจิ๋วมาก ต้องประคองทับปลา
                                                                     // นิ่งๆสักพัก (dwell) ก่อน progress ถึง
                                                                     // จะเริ่มขึ้น (เบ็ดอื่นขึ้นทันทีที่ทับ)
                                                                     // แต่พอหลุดจากปลา progress ร่วงช้ากว่า
                                                                     // เบ็ดทั่วไปเป็นการชดเชย
  // เบ็ดระดับ "endgame" ชุดใหม่ - ผู้ใช้ขอให้แพงสุด 2.5 ล้าน level สูงสุด 1000 ไล่ต่อจาก Legendary Rod แบบ
  // ทวีคูณ (~3 เท่าต่อขั้นทั้ง level และราคา) level 1000 ด้วยสูตร XP ปัจจุบัน (60*n*(n+1)/2) ต้องใช้ราวๆ 30
  // ล้าน XP - แทบเป็นไปไม่ได้จริงในทางปฏิบัติตั้งใจ เป็นเป้าหมาย "โชว์ของ" ระยะยาวสุดๆ เข้าธีมกับ Near
  // Impossible Rod พอดี (ดู runFischRodBrowseMenu ที่เช็ค levelRequired ก่อนให้ซื้อ) เบ็ด 6 ตัวนี้แตะทุกสเตต
  // เต็มๆ (รวม lureSpeed) เพราะเป็นเกียร์สุดยอด ไม่ควรมีจุดอ่อนเลย แถมได้ Wormhole (wormholeChancePercent)
  // ไล่ระดับ 5%->30% - โอกาสจับปลาเฉพาะเกาะอื่นได้แม้ไม่ได้อยู่ที่เกาะนั้นจริง (ดู fischPickFishForRarity)
  {"Ancient Rod",      20,  6300,  24,  22,  140,   3,  0,   0,  10, 260, 5, false},
  {"Mythic Rod",       60, 20000,  28,  26,  160,   4,  0,   0,  14, 340, 10, false},
  {"Celestial Rod",   150, 63000,  32,  30,  180,   4,  0,   0,  18, 450, 15, false},
  {"Void Rod",         320,200000,  36,  34,  200,   5,  0,   0,  22, 600, 20, false},
  {"Eternal Rod",      600,630000,  40,  38,  220,   5,  0,   0,  26, 800, 25, false},
  {"Absolute Rod",    1000,2500000, 45,  45,  250,   6,  0,   0,  30,1000, 30, false},
  // เบ็ดกลางใหม่ - เวอร์ชันเบาของ Wormhole (~9%) ให้ผู้เล่นกลางเกมได้ลองกลไกนี้ก่อนถึงเบ็ด endgame จริง ต่อ
  // ท้ายตารางเหมือนกัน (ไม่แทรกกลางๆ กัน index เบ็ดขำๆ/endgame ขยับ) ราคา/สเตตอยู่ระดับใกล้ Angler's/Prototype
  {"Wanderer's Rod",    1,   600,  12,  10,    0,   0,  0,   0,   8,  50, 9, false},
  // เบ็ด "มี mechanic พิเศษ" เฉพาะเกาะ 2 อัน/เกาะ (22 อัน) - level ลอยตัวไม่ผูกกับ level ของเกาะเอง (300+
  // ขึ้นไปเสมอ ไล่ตามลำดับเกาะ) ตั้งใจให้เป็น "รางวัลความเก่ง" แยกขาดจากการไปถึงเกาะนั้น ไม่ใช่ของที่ได้มา
  // พร้อมเกาะ - สเตตพื้นฐานเป็นแค่ตัวรองรับ (คำนวณสเกลตาม level คร่าวๆ) ของจริงมาจาก mechanic เฉพาะตัวที่
  // special-case ไว้ใน runFischReelPhase/runFischBiteWaitPhase/runFischFishingSequence (ดู
  // FISCH_ROD_IDX_ANCHOR เป็นต้นไป) ทั้งหมดต่อท้ายตารางเหมือนเบ็ด endgame กันไม่ให้ index อื่นขยับ
  // Home Dock (lvl300+)
  {"Anchor Rod",       300, 15000, 28, 28,  260,  3,  0,   0,  20, 260, 0, false},
  {"Tidecaller Rod",   320, 18000, 28, 28,  260,  3,  0,   0,  20, 260, 0, false},
  // Scatter Shoals (lvl350+)
  {"Shoal Twin Rod",   350, 25000, 32, 32,  290,  3,  0,   0,  23, 320, 0, false},
  {"Drift Net Rod",    370, 30000, 32, 32,  290,  3,  0,   0,  23, 320, 0, false},
  // Reef Point (lvl400+)
  {"Tidal Push Rod",   400, 40000, 36, 36,  320,  4,  0,   0,  26, 380, 0, false},
  {"Barrier Reef Rod", 420, 48000, 36, 36,  320,  4,  0,   0,  26, 380, 0, false},
  // Tide Hollow (lvl450+)
  {"Ebbtide Rod",      450, 65000, 40, 40,  350,  4,  0,   0,  29, 440, 0, false},
  {"Hollow Echo Rod",  470, 75000, 40, 40,  350,  4,  0,   0,  29, 440, 0, false},
  // Kettle Cove (lvl500+)
  {"Kettle Forged Rod",500,100000, 44, 44,  380,  4,  0,   0,  32, 500, 0, false},
  {"Simmer Rod",       520,115000, 44, 44,  380,  4,  0,   0,  32, 500, 0, false},
  // Bean Shoal (lvl550+)
  {"Bean Growth Rod",  550,150000, 48, 48,  410,  5,  0,   0,  35, 560, 0, false},
  {"Sprout Rod",       570,170000, 48, 48,  410,  5,  0,   0,  35, 560, 0, false},
  // Ring Islet (lvl600+)
  {"Ring Piercer Rod", 600,220000, 52, 52,  440,  5,  0,   0,  38, 620, 0, false},
  {"Halo Rod",         620,240000, 52, 52,  440,  5,  0,   0,  38, 620, 0, false},
  // Squirrel Isle (lvl650+)
  {"Acorn Guardian Rod",650,320000,56, 56,  470,  5,  0,   0,  41, 680, 0, false},
  {"Hoarder Rod",       670,350000,56, 56,  470,  5,  0,   0,  41, 680, 0, false},
  // Sentinel Ring (lvl750+)
  {"Sentinel Keeper Rod",750,500000,62,62,  520,  6,  0,   0,  46, 780, 0, false},
  {"Vault Warden Rod",   780,550000,62,62,  520,  6,  0,   0,  46, 780, 0, false},
  // Grand Shoal (lvl850+)
  // Grand Marshal Rod ไม่มี mechanic พิเศษ - "บัญชาทะเลทั้งผืน" แทนด้วยสเตตพื้นฐานที่สูงกว่าปกติ +10 ทุกด้าน
  {"Grand Marshal Rod", 850,850000, 78, 78,  570,  6,  0,   0,  61, 880, 0, false},
  {"Regal Rod",         880,950000, 68, 68,  570,  6,  0,   0,  51, 880, 0, false},
  // Farhaven (lvl1000+) - endgame side-grade เทียบเท่า Absolute Rod แต่มี mechanic เฉพาะตัวแทนสเตตดิบ
  {"Horizon Seeker Rod", 1000,1500000, 75, 75, 630, 7,  0,   0,  57,1000, 0, false},
  {"Abyssal Voyager Rod",1050,1800000, 75, 75, 630, 7,  0,   0,  57,1000, 0, false},
};
const int FISCH_ROD_COUNT = sizeof(FISCH_RODS) / sizeof(FISCH_RODS[0]); // ตอนนี้ 49 (27 เดิม + 22 เบ็ด mechanic เฉพาะเกาะ)
const int FISCH_ROD_SLOT_COUNT = 64; // เผื่อเพิ่มเบ็ดใหม่ในอนาคตโดยไม่ต้องขยาย struct อีก (กันข้อมูลเซฟหายซ้ำ)
                                      // ขยับจาก 32 เป็น 64 ตอนเพิ่มเบ็ด mechanic เฉพาะเกาะ 22 ตัว (49 ใกล้ชนแล้ว)
// index ตายตัวของเบ็ดขำๆสองอัน (ต้องตรงกับตำแหน่งจริงใน FISCH_RODS ด้านบนเป๊ะๆ) ใช้เช็คใน runFischReelPhase
// ว่าเบ็ดที่ใส่อยู่ตอนนี้ต้องใช้ logic พิเศษหรือเปล่า - ถ้าย้ายตำแหน่งแถวในตารางต้องแก้เลขนี้ตามด้วยเสมอ
const int FISCH_ROD_IDX_SO_HEAVY = 18;
const int FISCH_ROD_IDX_NEAR_IMPOSSIBLE = 19;
// index ตายตัวของเบ็ดที่มีความสามารถเฉพาะตัวอื่นๆ (เช็คด้วย equippedRodIndex เหมือนกัน) - เกณฑ์เดียวกัน
// เหมือนด้านบน ย้ายตำแหน่งแถวในตารางต้องแก้เลขพวกนี้ตามด้วยเสมอ
const int FISCH_ROD_IDX_PATIENT = 5;  // ค้างที่ 0 ไม่โดน tension เพิ่ม (ดู runFischReelPhase)
const int FISCH_ROD_IDX_TWITCHY = 9;  // surge-safe bonus x3 (ดู runFischReelPhase)
const int FISCH_ROD_IDX_ANGLERS = 13; // Precision Strike - แช่แข็งปลาเป็นระยะ (ดู runFischReelPhase)

// เบ็ด mechanic เฉพาะเกาะ 22 ตัว (ดูคอมเมนต์ตรงตาราง FISCH_RODS ด้านบน) - เกณฑ์เดียวกับด้านบนเป๊ะๆ
const int FISCH_ROD_IDX_ANCHOR = 27;          // Home Dock: bite wait ลดครึ่ง
const int FISCH_ROD_IDX_TIDECALLER = 28;      // Home Dock: progress +0.3/วิ เสมอไม่ว่าทับปลาหรือเปล่า
const int FISCH_ROD_IDX_SHOAL_TWIN = 29;      // Scatter Shoals: 15% ได้ปลาตัวเดิมซ้ำ 2 ตัว
const int FISCH_ROD_IDX_DRIFT_NET = 30;       // Scatter Shoals: น้ำหนักเอียงไปทาง max เสมอ
const int FISCH_ROD_IDX_TIDAL_PUSH = 31;      // Reef Point: ดันแถบจับเข้าหาปลาอัตโนมัติเป็นจังหวะ
const int FISCH_ROD_IDX_BARRIER_REEF = 32;    // Reef Point: ไม่โดน overload penalty เลย
const int FISCH_ROD_IDX_EBBTIDE = 33;         // Tide Hollow: หลุดครั้งก่อน รอบถัดไป rateIn +30%
const int FISCH_ROD_IDX_HOLLOW_ECHO = 34;     // Tide Hollow: progress จะตาย 0 ได้ 1 ครั้ง/ทริป กู้กลับ 50%
const int FISCH_ROD_IDX_KETTLE_FORGED = 35;   // Kettle Cove: tension เต็ม 100 รีเซ็ตได้ 1 ครั้ง/ทริป
const int FISCH_ROD_IDX_SIMMER = 36;          // Kettle Cove: ยิ่งทับปลาต่อเนื่องนาน rateIn ยิ่งเพิ่ม
const int FISCH_ROD_IDX_BEAN_GROWTH = 37;     // Bean Shoal: Perfect/Well ติดกันสะสม luck (Momentum)
const int FISCH_ROD_IDX_SPROUT = 38;          // Bean Shoal: ไม่เคยได้ปลาไม่กลายพันธุ์เลย
const int FISCH_ROD_IDX_RING_PIERCER = 39;    // Ring Islet: สุ่ม "แทง" ได้ progress + stun ปลา
const int FISCH_ROD_IDX_HALO = 40;            // Ring Islet: โชว์ชื่อปลาระหว่าง Reel เลย (ปกติซ่อน)
const int FISCH_ROD_IDX_ACORN_GUARDIAN = 41;  // Squirrel Isle: ปลายิ่งยากยิ่งได้โบนัสเยอะ (Berserk)
const int FISCH_ROD_IDX_HOARDER = 42;         // Squirrel Isle: ได้เหรียญโบนัสเล็กน้อยทุกครั้งที่จับได้
const int FISCH_ROD_IDX_SENTINEL_KEEPER = 43; // Sentinel Ring: การันตี Rare+ ทุก 8 ตัว (pity timer)
const int FISCH_ROD_IDX_VAULT_WARDEN = 44;    // Sentinel Ring: โอกาส mutation เพิ่มพิเศษ
const int FISCH_ROD_IDX_GRAND_MARSHAL = 45;   // Grand Shoal: (สเตตล้วน ไม่มี mechanic พิเศษเพิ่ม)
const int FISCH_ROD_IDX_REGAL = 46;           // Grand Shoal: XP ที่ได้ +25% เพิ่มอีกทาง
const int FISCH_ROD_IDX_HORIZON_SEEKER = 47;  // Farhaven: โบนัส Perfect Catch คูณ 2
const int FISCH_ROD_IDX_ABYSSAL_VOYAGER = 48; // Farhaven: ภูมิคุ้มกัน zero-tension + overload พร้อมกัน

// ---------------------------------------------------------------------------
// ตารางเกาะ/แมพ - เหมือน rod/fish catalog เพิ่มเกาะใหม่ทีหลังได้แค่เพิ่ม element x/y คือตำแหน่งไอคอนบนจอ
// Travel (0..127, 0..63 เว้นขอบไว้หน่อย) index 0 (Home Dock) ถือว่ามีอยู่แล้วเสมอเหมือน Basic Rod - ใช้
// fish catalog ทั้ง 120 ชนิดเดิมตามปกติ ไม่ต้องแตะอะไร
//
// **ตอนนี้มีแค่ Home Dock + เกาะทดสอบ 1 เกาะ (Reef Point) ที่ยังไม่มีปลา/เบ็ดเฉพาะของตัวเองเลย** - แค่ใส่
// มาให้ระบบเดินทาง/ปลดล็อกมีอะไรให้ทดสอบจริง ปลา/เบ็ดเฉพาะเกาะ + เกาะเพิ่มเติมเป็นงานรอบถัดไปตามที่ตกลงกัน
// ไว้ (ทำหลังจากโครง Level/Map/Rod ready ก่อน)
// ---------------------------------------------------------------------------
struct FischMapEntry {
  const char* name;
  int levelRequired;
  uint32_t unlockCost;
  int x, y; // พิกัด "โลก" เสมือน 0..FISCH_TRAVEL_WORLD_SIZE (ไม่ใช่พิกัดจอ 128x64 ตรงๆอีกต่อไป) - มุมมองเป็น
            // กล้องมองจากบนลงล่างตามหัวผู้เล่น เป้าอยู่กลางจอตายตัวเสมอ ตัวแมพต่างหากที่เลื่อนใต้เป้า
            // (ดู drawFischTravelMapFrame) ทำให้มีพื้นที่วางเกาะกว้างขึ้นมากตอนเพิ่มเกาะใหม่ในอนาคต
};

// พิกัดทั้ง 11 เกาะอ้างอิงจากภาพจริงที่ผู้ใช้วาด (map.png / where-island.png) เรียงตาม levelRequired จากน้อย
// ไปมาก (ตรงกับลำดับที่โผล่ในเมนู Fish Log แบบแยกหมวดหมู่ด้วย) - index ในตารางนี้ (0-10) ต้องตรงกับ
// exclusiveMapIndex ของปลาประจำเกาะใน FISCH_CATALOG เป๊ะๆ ห้ามสลับลำดับเด็ดขาดถ้าไม่แก้ตรงนั้นด้วย
const FischMapEntry FISCH_MAPS[] = {
  {"Home Dock",      1,      0,  43,  54}, // เกาะเล็กมุมบนซ้าย
  {"Scatter Shoals", 6,    300, 110, 154}, // เกาะกลุ่มก้อนเล็กกระจาย
  {"Reef Point",     15,   900, 203,  50}, // เกาะรูปคลื่น/ภูเขา
  {"Tide Hollow",    30,  2500,  61, 305}, // เกาะซ้ายกลางล่าง
  {"Kettle Cove",    55,  6000, 236, 229}, // เกาะรูปเหยือก/หม้อ
  {"Bean Shoal",     90, 13000, 317, 312}, // เกาะรูปถั่ว/ไต
  {"Ring Islet",    140, 26000,  61, 437}, // เกาะมีรูตรงกลาง
  {"Squirrel Isle",  200, 48000, 442,  64}, // เกาะรูปกระรอก
  {"Sentinel Ring",  280, 85000, 441, 207}, // เกาะไอคอนวงซ้อนวง
  {"Grand Shoal",    380,140000, 267, 449}, // เกาะใหญ่สุด
  {"Farhaven",       500,220000, 433, 453}, // เกาะไกลสุด
};
const int FISCH_MAP_COUNT = sizeof(FISCH_MAPS) / sizeof(FISCH_MAPS[0]);
const int FISCH_MAP_SLOT_COUNT = 16; // เผื่อเพิ่มเกาะใหม่ในอนาคตโดยไม่ต้องขยาย struct อีก (ตอนนี้ใช้ 11/16)
const int FISCH_TRAVEL_WORLD_SIZE = 500; // ขนาดโลกเสมือนของแมพเดินทาง (0..500 ทั้งสองแกน)

// สุ่มค่าที่เป็นไปได้ของ Random Rod ต่อ 1 ด้าน (ดี-แย่คละกันไปหมด ไม่ผูกกับตัวเลขในตาราง FISCH_RODS เลย) -
// progressRateBonus ของ Random Rod จำกัดไว้แคบกว่าเบ็ดขำ 2 อันด้านบนมาก (แค่ ±15) ให้ยังเล่นได้จริง ไม่ใช่
// สุ่มไปเจอค่าเทพ/พังระดับ ±900 แบบนั้น - MIN_STAT/MAX_STAT ใช้ร่วมกันทั้ง resilience/luck/lureSpeed (สเตต
// ทั่วไปที่มีสเกลใกล้เคียงกัน ไม่ต้องแยกช่วงสุ่มต่างหากทีละตัว)
const int FISCH_RANDOM_ROD_MIN_STAT = -15;
const int FISCH_RANDOM_ROD_MAX_STAT = 25;
const int FISCH_RANDOM_ROD_MIN_HOOK_MS = -100;
const int FISCH_RANDOM_ROD_MAX_HOOK_MS = 150;
const int FISCH_RANDOM_ROD_MIN_MISS = -2;
const int FISCH_RANDOM_ROD_MAX_MISS = 3;
const int FISCH_RANDOM_ROD_MIN_PROGRESS_RATE = -15;
const int FISCH_RANDOM_ROD_MAX_PROGRESS_RATE = 15;
const int FISCH_RANDOM_ROD_MIN_CONTROL = -4;
const int FISCH_RANDOM_ROD_MAX_CONTROL = 8;
const float FISCH_RANDOM_ROD_MIN_WEIGHT = 5.0f;  // ไม่ครอบคลุมทั้งช่วง (Legendary หนักสุด 181.5kg) ตั้งใจ -
const float FISCH_RANDOM_ROD_MAX_WEIGHT = 80.0f;  // ให้ยังมีความเสี่ยง "สุ่มได้เบ็ดที่รับปลาใหญ่สุดไม่ไหว" อยู่

// ---------------------------------------------------------------------------
// Rod Mastery (แบบย่อ) - นับจำนวนจับสำเร็จต่อเบ็ด (fischCache.rodMasteryCatches[]) ถึงหมุดหมายปลดล็อกโบนัส
// ถาวรติดเบ็ดช่องนั้นไปตลอด ใช้สูตรเดียวกันทุกเบ็ด (ไม่ได้ออกแบบ reward เฉพาะทีละ 27 เบ็ด - ใหญ่เกินไปตอนนี้)
// เลือกบวก Luck กับ Max Weight เพราะสองสเตตนี้มีความหมายกับทุกเบ็ดเหมือนกันหมด (ต่างจาก เช่น Control ที่บาง
// เบ็ดใช้เป็นกลไกพิเศษอยู่แล้ว) ดู fischResolveEquippedRodStats
// ---------------------------------------------------------------------------
const int FISCH_MASTERY_BRONZE_CATCHES = 50;
const int FISCH_MASTERY_GOLD_CATCHES = 200;
const int FISCH_MASTERY_BRONZE_LUCK = 2;
const float FISCH_MASTERY_BRONZE_WEIGHT = 5.0f;
const int FISCH_MASTERY_GOLD_LUCK = 5;
const float FISCH_MASTERY_GOLD_WEIGHT = 15.0f;

/// คืนโบนัส Luck/Max Weight จาก Rod Mastery ตามจำนวนจับสำเร็จด้วยเบ็ดช่องนั้น (ผ่าน reference เพราะ C++ คืน
/// ได้ค่าเดียว) ไม่ stack กัน - gold แทนที่ bronze ไปเลยเมื่อถึงหมุดหมายที่สูงกว่า
void fischGetMasteryBonus(int catches, int& luckOut, float& weightOut) {
  if (catches >= FISCH_MASTERY_GOLD_CATCHES) {
    luckOut = FISCH_MASTERY_GOLD_LUCK;
    weightOut = FISCH_MASTERY_GOLD_WEIGHT;
  } else if (catches >= FISCH_MASTERY_BRONZE_CATCHES) {
    luckOut = FISCH_MASTERY_BRONZE_LUCK;
    weightOut = FISCH_MASTERY_BRONZE_WEIGHT;
  } else {
    luckOut = 0;
    weightOut = 0.0f;
  }
}

// ---------------------------------------------------------------------------
// Mutation - ปลาที่จับได้มีโอกาสสุ่ม "กลายพันธุ์" เพิ่มราคาขาย/XP อีกทาง แยกอิสระจาก rarity เลย (ปลา Common
// ก็เป็น Ethereal ได้) งานวิจัยจาก Fisch จริง (fischipedia.org/wiki/Mutations) - ของจริงมีหลายสิบแบบคูณสูงสุด
// 15x ปรับสเกลลงมาให้เข้ากับเศรษฐกิจเรา ม้วนเดียว roll 0-999 เทียบจากหายากสุดไปหาง่ายสุด (เหมือน
// fischRollRarity) เกาะที่ level สูงมีโบนัสโอกาสเพิ่มนิดหน่อย (ดู fischRollMutation) ตามที่ผู้ใช้ขอ "ผูกกับ
// เกาะ" - ไม่ได้ผูกกับเบ็ดเพิ่มอีกเพราะเบ็ดมีความสามารถเฉพาะตัวเยอะพอแล้ว (Wormhole/Precision Strike/ฯลฯ)
// ---------------------------------------------------------------------------
struct FischMutationEntry {
  const char* name;
  float multiplier;
  int rollThreshold; // roll (0-999) >= ค่านี้ = ได้ mutation นี้ (เช็คจากบนลงล่าง เจอก่อนสุดใช้อันนั้น)
};
const FischMutationEntry FISCH_MUTATIONS[] = {
  {"Ethereal",  12.0f, 999}, // 0.1%
  {"Ancient",    8.0f, 997}, // 0.2%
  {"Prismatic",  5.0f, 990}, // 0.7%
  {"Radiant",    3.0f, 970}, // 2%
  {"Golden",     2.0f, 920}, // 5%
};
const int FISCH_MUTATION_COUNT = sizeof(FISCH_MUTATIONS) / sizeof(FISCH_MUTATIONS[0]);
const int FISCH_MUTATION_ISLAND_BONUS_DIVISOR = 50; // ยิ่งเกาะ levelRequired สูง ยิ่งได้ roll บวกเพิ่มนิดหน่อย

// ---------------------------------------------------------------------------
// Bait (เหยื่อ) - ไม่ใส่เหยื่อ = โดนบทลงโทษเล็กน้อย (FISCH_NO_BAIT_*_PENALTY) เหยื่อพื้นฐานที่สุดแค่ดึงกลับมา
// เท่า baseline ปกติพอดี เหยื่อแพงกว่านั้นถึงจะดีกว่าปกติจริง - ต่างจากเบ็ด: ซื้อครั้งเดียวใช้ได้ตลอดไม่มีวัน
// หมด (ไม่ทำระบบ "ใช้แล้วหมดต้องซื้อใหม่" แบบเกมจริง เพื่อความง่าย)
//
// เหยื่อทั่วไป (exclusiveMapIndex -1) ซื้อได้ที่ Shop กลาง เหมือนเบ็ด ส่วนเหยื่อเฉพาะเกาะ (เกาะละ 1 ชนิด)
// ซื้อได้เฉพาะตอนอยู่ที่เกาะนั้นเท่านั้น (ดู runFischTravelIslandDetail > Shop) ธีมตามชื่อเกาะ ราคา/สเตต
// ไล่ระดับตามลำดับเกาะเหมือนปลาประจำเกาะ
// ---------------------------------------------------------------------------
struct FischBaitEntry {
  const char* name;
  uint32_t cost;
  int luckBonus;
  int lureSpeedBonus;
  int exclusiveMapIndex; // -1 = ซื้อได้ทุกที่ (Shop กลาง) >=0 = ซื้อได้เฉพาะที่เกาะนั้น (index ตรงกับ FISCH_MAPS)
};
const FischBaitEntry FISCH_BAITS[] = {
  // ทั่วไป (5 ชนิด) - หาซื้อได้ที่ Shop กลางเสมอ ไม่ผูกเกาะ
  {"Basic Bait",    50,  8,  8, -1}, // หักล้าง NO_BAIT_PENALTY พอดี = "ปกติ"
  {"Worm Bait",    200, 15, 12, -1},
  {"Shrimp Bait",  600, 22, 18, -1},
  {"Magnet Bait", 1800, 30, 25, -1},
  {"Golden Bait", 5000, 45, 35, -1},
  // เฉพาะเกาะ (เกาะละ 1 ชนิด) - ธีมตามชื่อเกาะ ราคา/สเตตไล่ระดับตามลำดับเกาะ (index ต้องตรงกับ FISCH_MAPS)
  {"Dock Worm",        80, 10, 10, 0},
  {"Shoal Chum",       250, 16, 13, 1},
  {"Coral Bait",       600, 22, 18, 2},
  {"Hollow Shrimp",   1200, 28, 23, 3},
  {"Kettle Broth",    2200, 34, 28, 4},
  {"Bean Paste",       3800, 40, 33, 5},
  {"Ring Lure",         6000, 46, 38, 6},
  {"Acorn Mix",         9000, 52, 43, 7},
  {"Vault Chum",       13000, 58, 48, 8},
  {"Grand Chum",       18000, 64, 53, 9},
  {"Farhaven Essence", 25000, 70, 58, 10},
};
const int FISCH_BAIT_COUNT = sizeof(FISCH_BAITS) / sizeof(FISCH_BAITS[0]); // 16 (5 ทั่วไป + 11 เฉพาะเกาะ)
const int FISCH_BAIT_SLOT_COUNT = 20; // เผื่อเพิ่มเหยื่อใหม่ในอนาคต (ตอนนี้ใช้ 16/20)
const int FISCH_NO_BAIT_LUCK_PENALTY = -8;
const int FISCH_NO_BAIT_LURE_PENALTY = -8;

// ---------------------------------------------------------------------------
// ปลาหนึ่งตัวที่ถืออยู่จริงในกระเป๋า (catalogIndex + น้ำหนักของตัวนั้นเป๊ะๆ) catalogIndex = -1 หมายถึง
// "ช่องว่าง" (ไม่มีปลา) ดู FISCH_INVENTORY_CAPACITY ด้านล่าง
// ---------------------------------------------------------------------------
struct FischFishItem {
  int8_t catalogIndex;
  float weightKg;
  int8_t mutationIndex; // -1 = ปลาปกติ ไม่กลายพันธุ์ (ดู FISCH_MUTATIONS) ค้างติดกับตัวนี้ไปจนกว่าจะขาย
};
const int FISCH_INVENTORY_CAPACITY = 40;

// ---------------------------------------------------------------------------
// ข้อมูลเซฟถาวร - เก็บเป็น blob เดียวผ่าน Preferences namespace "fisch" (putBytes/getBytes) แทนที่จะ
// แยกคีย์ต่อปลา (จะกลายเป็น 30+ คีย์) bestWeightKg[i] <= 0 หมายถึง "ยังไม่เคยจับชนิดนี้เลย" (ใช้แทน
// discovered flag แยก เพราะน้ำหนักปลาจริงเป็นบวกเสมออยู่แล้ว) inventory[] คือปลาที่ถืออยู่จริงตอนนี้
// (เปลี่ยนจากเดิมที่นับแค่จำนวนต่อชนิด - ต้องรู้น้ำหนักแต่ละตัวจริงเพราะราคาขายอิงน้ำหนักด้วย)
// rodOwned[] ไม่รวม index 0 (Basic Rod ถือว่ามีเสมอ ไม่ต้องเก็บสถานะ) equippedRodIndex คือเบ็ดที่ใส่อยู่
// ตอนนี้ (default 0 = Basic Rod พอดีกับตอน zero-init/reset)
// ---------------------------------------------------------------------------
struct FischSaveData {
  float bestWeightKg[FISCH_CATALOG_COUNT];
  uint32_t totalCasts;
  uint32_t totalCatches;
  uint32_t totalEscapes;
  uint32_t coins;
  uint32_t totalXp; // เลเวลคำนวณจากค่านี้เสมอ (ดู fischLevelForXp) ไม่เก็บ level แยกกันจะได้ไม่มีทางไม่ตรงกัน
  FischFishItem inventory[FISCH_INVENTORY_CAPACITY];
  uint8_t inventoryCount;
  bool rodOwned[FISCH_ROD_SLOT_COUNT];
  uint8_t equippedRodIndex;
  bool mapUnlocked[FISCH_MAP_SLOT_COUNT]; // index 0 (Home Dock) ไม่ใช้ ถือว่ามีเสมอ (ดู fischOwnsMap)
  uint8_t currentMapIndex;                // เกาะที่กำลังตกปลาอยู่ตอนนี้ (default 0 = Home Dock พอดีกับ zero-init)
  uint16_t rodMasteryCatches[FISCH_ROD_SLOT_COUNT]; // จับสำเร็จกี่ตัวด้วยเบ็ดช่องนั้น (ดู fischMasteryBonus)
  bool baitOwned[FISCH_BAIT_SLOT_COUNT];
  int8_t equippedBaitIndex; // -1 = ไม่ได้ใส่เหยื่อ (ค่าเริ่มต้น - ต่างจากเบ็ดที่ default 0 ตรงนี้ตั้งเองใน
                             // fischResetSaveDataToEmpty ไม่พึ่ง zero-init เพราะ 0 จะหมายถึงเหยื่อช่องแรกพอดี)
};

FischSaveData fischCache;

// Debug Mode > Force Mutation (ดู runFischDebugMenu) - ประกาศไว้ตรงนี้เพราะ fischRollMutation() อยู่ก่อน
// จุดที่ Debug Mode ฟอร์แมตประกาศตัวแปรอื่นๆของมันด้านล่างไฟล์มาก (ตัวแปร global ต้องประกาศก่อนใช้จริงเสมอ)
int fischDebugForceMutation = -1; // -1 = ปิด, 0..FISCH_MUTATION_COUNT-1 = บังคับ mutation นั้นเสมอ

/// -1 = ไม่กลายพันธุ์, else = index ใน FISCH_MUTATIONS
int fischRollMutation() {
  if (fischDebugForceMutation >= 0) return fischDebugForceMutation; // Debug Mode > Force Mutation
  int bonus = FISCH_MAPS[fischCache.currentMapIndex].levelRequired / FISCH_MUTATION_ISLAND_BONUS_DIVISOR;
  // Vault Warden Rod เท่านั้น (ดู FISCH_ROD_IDX_VAULT_WARDEN) - โบนัสโอกาส mutation เพิ่มอีกทาง สะสมกับ
  // โบนัสจากเกาะด้านบนได้เลย
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_VAULT_WARDEN) bonus += 60;
  int roll = random(0, 1000) + bonus;
  int result = -1;
  for (int i = 0; i < FISCH_MUTATION_COUNT; i++) {
    if (roll >= FISCH_MUTATIONS[i].rollThreshold) { result = i; break; }
  }
  // Sprout Rod เท่านั้น (ดู FISCH_ROD_IDX_SPROUT) - ไม่เคยได้ปลาที่ไม่กลายพันธุ์เลย อย่างน้อยได้ tier ต่ำสุด
  // (Golden - ตัวสุดท้ายในตาราง FISCH_MUTATIONS ที่เรียงจากหายากสุดไปหาง่ายสุด) เสมอ
  if (result == -1 && fischCache.equippedRodIndex == FISCH_ROD_IDX_SPROUT) {
    result = FISCH_MUTATION_COUNT - 1;
  }
  return result;
}

typedef void (*FischDrawFn)();

// ---------------------------------------------------------------------------
// สถานะ global ของเกม - ใช้แทนการส่ง parameter ข้ามฟังก์ชันเยอะๆ (ตรงกับสไตล์ที่ไฟล์ .ino หลักใช้อยู่
// แล้วทั้งไฟล์ เช่น selectedIndex, wifiConnectCancelled) เพราะ draw function ทุกตัวต้องมี signature
// ไม่รับ parameter เลย (void->void) ให้ตรงกับ FischDrawFn เพื่อใช้กับ fischTransitionTo ได้
// ---------------------------------------------------------------------------
bool fischQuitToMenu = false; // true = ผู้เล่นยืนยัน "Quit fishing?" ระหว่างเฟสใดก็ตาม - เช็คหลังเรียกทุกเฟส

int fischMenuIndex = 0;
const char* const FISCH_MAIN_ITEMS[] = { "Go Fishing", "Travel", "Fish Log", "Inventory", "Shop", "Stats", "Settings" };
const int FISCH_MAIN_ITEM_COUNT = 7;

bool fischLoadingReady = false;
int fischLoadingFrameIndex = 0; // เฟรมปัจจุบันของแอนิเมชันปลา (gif_fisch_loading.h)
int fischLoadingDots = 0;       // จุดไข่ปลาท้าย "Loading" - ขยับคนละจังหวะกับเฟรมปลา (ไวกว่า)

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
unsigned long fischReelOverlapSinceMs = 0; // เวลาที่เริ่มทับปลาต่อเนื่อง (0 = ไม่ได้ทับอยู่) ใช้กับ Near
                                            // Impossible Rod เท่านั้น (ดู FISCH_NEAR_IMPOSSIBLE_DWELL_MS)
bool fischReelOverloaded = false; // true = ปลาหนักเกิน maxWeightKg ของเบ็ดที่ใส่อยู่ (ดู runFischReelPhase)

float fischReelTension = 0;             // 0..100 - เต็มแล้ว "เบ็ดขาด" ปลาหลุดทันที (ดู runFischReelPhase)
unsigned long fischNextSurgeAtMs = 0;    // เวลา (absolute, millis()) ที่ surge รอบถัดไปจะเกิด
bool fischSurgeWarningActive = false;    // true ระหว่างช่วงเตือนสั้นๆก่อน surge จริงจะเกิด

// Angler's Rod เท่านั้น (Precision Strike - ดู FISCH_ROD_IDX_ANGLERS) แช่แข็งปลาให้นิ่งเป็นระยะๆ (คนละแบบ
// กับ surge ที่เป็นอันตราย - อันนี้ช่วยผู้เล่นล้วนๆ) reset ทุกครั้งที่เริ่ม Reel ใหม่/กลับจาก dialog
unsigned long fischNextPrecisionStrikeAtMs = 0;
unsigned long fischPrecisionFreezeUntilMs = 0;
bool fischReelPrecisionFrozen = false; // ให้ drawFischReelFrame อ่านได้ (คำนวณในลูปหลักของ runFischReelPhase)

// สเตตของเบ็ด mechanic เฉพาะเกาะ 22 ตัว (ดู FISCH_ROD_IDX_ANCHOR เป็นต้นไป) - ตัวที่มี "ต่อทริป" reset ใน
// runFischReelPhase ทุกครั้ง ส่วนตัวที่ไม่มีคอมเมนต์กำกับ = runtime ล้วนๆข้ามทริปได้ (ไม่บันทึกลงเซฟ)
bool fischSecondWindUsed = false;    // Hollow Echo Rod - ต่อทริป
bool fischTensionResetUsed = false;  // Kettle Forged Rod - ต่อทริป
bool fischTenacityActive = false;    // Ebbtide Rod - ตั้งตอนปลาหลุด ใช้ตอนเริ่ม Reel รอบถัดไปแล้วเคลียร์
unsigned long fischSimmerHoldSinceMs = 0; // Simmer Rod - เวลาที่เริ่มทับปลาต่อเนื่อง (แยกจาก Near Impossible)
unsigned long fischNextTidalPushAtMs = 0; // Tidal Push Rod - ต่อทริป
int fischMomentumStackCount = 0;     // Bean Growth Rod - สะสมข้ามทริปได้ รีเซ็ตตอนจับพลาด/ธรรมดา
int fischSentinelPityCount = 0;      // Sentinel Keeper Rod - นับจับสะสมตั้งแต่ได้ Rare+ ครั้งล่าสุด

float fischCaughtWeight = 0;
bool fischCaughtIsNewSpecies = false;
bool fischCaughtIsNewBest = false;
int fischCaughtMutationIndex = -1; // -1 = ไม่กลายพันธุ์ (ดู fischRollMutation)
bool fischCaughtPerfect = false;   // progress ไม่เคยลดลงเลยตลอด Reel (ดู runFischReelPhase)
bool fischCaughtWell = false;      // ไม่ถึงขั้น Perfect แต่ progress ไม่เคยตกต่ำกว่า 50% หลังแตะ 50% ครั้งแรก

int fischLogIndex = 0;
int fischLogCategoryIndex = 0; // 0=All, 1=General, 2..=รายเกาะ (ดู fischLogCategoryName/runFischLogMenu)
int fischInvIndex = 0;
int fischSettingsIndex = 0;
int fischDebugIndex = 0;
bool fischMomentumEnabled = true; // Settings > Reel Momentum - ปิดได้เผื่อใครชอบแถบ reel แบบแข็งๆดั้งเดิม

// Debug Mode (Settings > Debug Mode) - ตัวแปร runtime ล้วนๆ ไม่บันทึกลง save data (ดูรายละเอียดที่
// runFischDebugMenu) ประกาศไว้ตรงนี้เพราะ fischOwnsRod/fischOwnsMap/fischRollRarity เรียกใช้ตั้งแต่ก่อน
// หน้าเมนู Debug เองในไฟล์ - ตัวแปร global ต้องประกาศก่อนจุดที่ใช้งานจริงเสมอ (ไม่เหมือนฟังก์ชัน)
bool fischDebugUnlockAllRods = false;
bool fischDebugUnlockAllIslands = false;
int fischDebugForceRarity = -1; // -1 = ปิด, 0..4 = บังคับ rarity นั้นเสมอ (ดู fischRollRarity)

// ค่าพลังจริงของเบ็ดที่ใส่อยู่ตอนนี้ - คำนวณครั้งเดียวตอนเริ่ม Go Fishing แต่ละรอบ (ดู
// fischResolveEquippedRodStats) ทุกเฟส (Cast/Hook/Reel) อ่านจากตัวแปรพวกนี้ตรงๆ ไม่อ่าน FISCH_RODS เอง
int fischActiveResilienceBonus = 0;
int fischActiveLuckBonus = 0;
int fischActiveHookMsBonus = 0;
int fischActiveHookMissBonus = 0;
int fischActiveControlBonus = 0;      // หน่วย: 1 = 0.01 (ดู FischRodEntry.controlBonus)
int fischActiveProgressRateBonus = 0; // บวกตรงเข้า rateIn (ดู FischRodEntry.progressRateBonus)
int fischActiveLureSpeedBonus = 0;    // ลดเวลารอกัดใน Bite phase (ดู FischRodEntry.lureSpeedBonus)
float fischActiveMaxWeightKg = 0;     // 0 = ไม่จำกัด (ดู FischRodEntry.maxWeightKg / fischReelOverloaded)
int fischActiveWormholeChancePercent = 0; // ดู FischRodEntry.wormholeChancePercent / fischPickFishForRarity

// fischInvIndex (ประกาศไว้แล้วด้านบน) กับ fischInvItemIndex/fischRodBrowseIndex ด้านล่าง ใช้ร่วมกันทั้ง
// Inventory และ Shop (สองหน้านี้ไม่เปิดพร้อมกันอยู่แล้ว เลยไม่ต้องแยกตัวแปรซ้ำ) - fischFishBrowseSellMode
// บอกว่าหน้าไล่ดูปลาตอนนี้กำลังถูกยืมมาใช้จาก Shop (มีปุ่มขาย) หรือ Inventory (ดูอย่างเดียว)
// fischRodBrowseShopMode บอกแบบเดียวกันสำหรับหน้าไล่ดูเบ็ด (ซื้อได้ vs ใส่ได้อย่างเดียว)
int fischInvCategoryIndex = 0;   // 0=Fish, 1=Rod, 2=Bait - เมนูย่อยแรกที่เจอตอนกด Inventory
int fischInvItemIndex = 0;       // ตัวที่เท่าไหร่ของชนิดที่เลือกไว้ (fischInvIndex) ตอนไล่ดูราย ตัว
int fischRodBrowseIndex = 0;     // ตำแหน่ง cursor ตอนไล่ดู/เลือกเบ็ด
int fischBaitBrowseIndex = -1;   // ตำแหน่ง cursor ตอนไล่ดู/เลือกเหยื่อ -1 = "No Bait" (ตัวเลือกพิเศษเสมอ)

bool fischFishBrowseSellMode = false; // true = เปิดจาก Shop > Sell Fish (มีปุ่มขาย)
bool fischRodBrowseShopMode = false;  // true = เปิดจาก Shop > Buy Rods (ซื้อได้ด้วย ไม่ใช่แค่ใส่)
bool fischBaitBrowseShopMode = false; // true = เปิดจาก Shop > Buy Bait (ซื้อได้ด้วย ไม่ใช่แค่ใส่)

int fischShopMenuIndex = 0; // 0=Sell Fish, 1=Buy Rods, 2=Buy Bait

// Travel/reticle - ดู runFischTravelMapMenu ผู้ใช้ขอมาเป็นพิเศษ: กด < > เฉยๆขยับแนวนอน, กด SW_OK ค้าง
// แล้วกด < > ขยับแนวตั้ง (ใช้ปุ่มเดิม 2 แกน ผ่านตัวปรับ ไม่ต้องมีปุ่มใหม่) พอเป้าทับเกาะไหนอยู่ กด SW_OK
// เฉยๆ (กด-ปล่อย ไม่มีการกด < > คั่นกลาง - ถ้ามีจะถือว่าเป็นการปรับแกน Y ไม่ใช่กดยืนยัน) เพื่อเข้าเกาะนั้น
// พิกัดนี้คือตำแหน่ง "โลก" เสมือน (0..FISCH_TRAVEL_WORLD_SIZE) ไม่ใช่พิกัดจอ - บนจอเป้าจะอยู่กลางตายตัวเสมอ
int fischTravelReticleX = 64;
int fischTravelReticleY = 32;
int fischTravelLastNear = -1;  // index เกาะที่เป้าอยู่ใกล้ตอนนี้ (-1 = ไม่มี) - ใช้โชว์ชื่อเกาะบนจอ
int fischTravelDetailIdx = -1; // เกาะที่กำลังดูรายละเอียดอยู่ (ตั้งก่อนเรียก runFischTravelIslandDetail)

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

// Tension/surge (เบ็ดขาด) - งานวิจัยจาก Fisch (Roblox) จริง: มีช่วง "ไฟแดง" เตือนล่วงหน้าสั้นๆ กด OK ค้าง
// ผ่านช่วงนั้นเสีย tension เยอะเป็นพิเศษ ปล่อยทันเวลาไม่เสีย - ทำให้กด OK ค้างมี "ต้นทุน" จริง แก้ปัญหาที่
// เจอว่าแค่ปล่อยให้แถบจับผ่านปลาเฉยๆเป็นครั้งคราว (ไม่ต้องกดค้างจริงจัง) ก็ยื้อเกมได้ฟรีไม่มีต้นทุนอะไรเลย
const unsigned long FISCH_SURGE_WARNING_LEAD_MS = 400;
const unsigned long FISCH_SURGE_MIN_INTERVAL_MS = 1500;
const unsigned long FISCH_SURGE_MAX_INTERVAL_MS = 3000;
const float FISCH_SURGE_PENALTY = 35.0f;    // tension ที่เพิ่มทันทีถ้ายังกด OK ค้างอยู่ตอนโดน surge
const float FISCH_SURGE_SAFE_BONUS = 5.0f;  // progress โบนัสเล็กน้อยถ้าปล่อยทันเวลา ("อ่านจังหวะถูก")
const float FISCH_TWITCHY_SURGE_BONUS_MULT = 3.0f; // Twitchy Rod เท่านั้น (ดู FISCH_ROD_IDX_TWITCHY)

// Angler's Rod เท่านั้น (Precision Strike - ดู FISCH_ROD_IDX_ANGLERS) แช่แข็งปลานิ่งเป็นจังหวะสม่ำเสมอ
// (ไม่ใช่สุ่มแบบ surge) ให้ประคองแถบจับได้ง่ายขึ้นเป็นช่วงๆ
const unsigned long FISCH_PRECISION_STRIKE_INTERVAL_MS = 4000;
const unsigned long FISCH_PRECISION_STRIKE_FREEZE_MS = 1000;

// เบ็ด mechanic เฉพาะเกาะ - ค่าคงที่ที่ต้องใช้ร่วม (ดู FISCH_ROD_IDX_* คอมเมนต์แต่ละตัวอธิบายไว้แล้ว)
const unsigned long FISCH_TIDAL_PUSH_INTERVAL_MS = 3000;
const float FISCH_TIDAL_PUSH_FRACTION = 0.4f;      // ดันแถบจับเข้าใกล้ปลากี่เปอร์เซ็นต์ของระยะห่างต่อครั้ง
const float FISCH_TIDECALLER_FLAT_RATE = 0.3f;     // ต่อวิ ไม่ว่าทับปลาหรือเปล่า
const int FISCH_RING_PIERCER_STAB_CHANCE = 20;     // % ต่อครั้งที่ปลาเปลี่ยนทิศ
const float FISCH_RING_PIERCER_STAB_PROGRESS = 8.0f;
const float FISCH_SIMMER_RAMP_PER_SEC = 3.0f;      // rateIn เพิ่มต่อวิที่ทับปลาต่อเนื่อง
const float FISCH_SIMMER_RAMP_CAP = 24.0f;         // เพดานโบนัสสะสมสูงสุด
const float FISCH_ACORN_BERSERK_PER_DIFF = 0.2f;   // rateIn เพิ่มต่อ 1 หน่วย reelDifficulty ของปลา
const float FISCH_HOLLOW_ECHO_RECOVER_TO = 50.0f;  // กู้ progress กลับมาเท่านี้ (ครั้งเดียว/ทริป)
// progress ค้างที่ 0 = ปลาดิ้นหนักขึ้น สายก็ตึงขึ้นตามทันที (ไม่มีดีเลย์แล้ว) เดิมมีตัวจับเวลาแยก "ค้างเกิน
// 3 วิ = หลุดทันที" คนละระบบกับ tension เลย แต่ผู้ใช้บอกว่ามันคนละเรื่องกันไปเปล่าๆ ยุบรวมเป็นทางเดียว: จบ
// เกมด้วย tension เต็ม 100 เงื่อนไขเดียวเท่านั้น (ดู runFischReelPhase)
const float FISCH_ZERO_TENSION_RISE_RATE = 5.0f; // tension ต่อวินาทีตอน progress ค้างที่ 0
// ฟิสิกส์แถบจับ (ผู้เล่นคุม) - ใช้ความเร่ง+แรงโน้มถ่วง+เด้งแบบมี damping แทนการขยับตำแหน่งตรงๆทุก tick
// (แบบเดิมมันแข็งเพราะไม่มี momentum เลย) ดู runFischReelPhase
const float FISCH_CAPTURE_ACCEL = 2.6f;   // หน่วย/วิ² ตอนกด OK ค้าง (ดันเข้าหาปลา)
const float FISCH_CAPTURE_GRAVITY = 2.0f; // หน่วย/วิ² ตอนปล่อย (ดึงกลับ)
const float FISCH_CAPTURE_MAX_SPEED = 1.6f;
const float FISCH_BOUNCE_DAMPING = 0.55f; // ชนขอบแล้วเด้งกลับ แต่แรงลดลงทุกครั้ง ไม่ใช่สะท้อนเต็มแรงตลอดไป

const unsigned long FISCH_HOLD_REPEAT_DELAY_MS = 400;    // กดค้างนานแค่ไหนก่อนเริ่ม auto-repeat (Fish Log/Inventory)
const unsigned long FISCH_HOLD_REPEAT_INTERVAL_MS = 90;  // ทุกกี่ ms ต่อก้าวหลังจากนั้น

// เบ็ดหนักเกินรับไหว (ดู FischRodEntry.maxWeightKg) - tension ขึ้นเพิ่มอีกทางตลอดเวลาไม่ว่ากด OK อยู่หรือ
// เปล่า (สายเครียดจากน้ำหนักปลาตรงๆ) ยิ่งหนักเกินเท่าไหร่ยิ่งแรง แต่มีเพดานกันพุ่งเร็วเกินจนแทบไม่มีโอกาส
const float FISCH_OVERLOAD_TENSION_PER_EXCESS = 40.0f; // ต่อสัดส่วนน้ำหนักที่เกิน (1.0 = เกิน 100%)
const float FISCH_OVERLOAD_TENSION_CAP = 50.0f;        // เพดานอัตราเพิ่มสูงสุดต่อวิ

// เบ็ดขำๆ 2 อัน (So Heavy Rod / Near Impossible Rod) - พฤติกรรมพิเศษเฉพาะตัว ไม่ใช้สูตร rateIn/rateOut
// ปกติ ดู FISCH_ROD_IDX_SO_HEAVY/_NEAR_IMPOSSIBLE และ runFischReelPhase
const float FISCH_SO_HEAVY_FLAT_RATE = 0.5f;              // progress ขึ้นคงที่ ~+1 ทุก 2 วิ ไม่สนตำแหน่งแถบจับ
const unsigned long FISCH_NEAR_IMPOSSIBLE_DWELL_MS = 1000; // ต้องทับปลาต่อเนื่องนานเท่านี้ก่อน progress ถึงจะขึ้น
const float FISCH_NEAR_IMPOSSIBLE_LOSS_MULT = 0.35f;       // progress ร่วงตอนหลุดช้ากว่าเบ็ดทั่วไป (35% ของปกติ)

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS - ดูคอมเมนต์หัวไฟล์ว่าทำไมจำเป็น เพิ่มฟังก์ชันใหม่ในไฟล์นี้ = ต้องเพิ่มบรรทัดที่นี่ด้วย
// ---------------------------------------------------------------------------
void fischResetSaveDataToEmpty();
void fischLoadSaveData();
void fischSaveSaveData();
int fischFindBestCatchIndex();
void fischGetMasteryBonus(int catches, int& luckOut, float& weightOut);
int fischRollMutation();

void fischMaskOutsideCircle(int r);
void fischIrisClose();
void fischIrisOpen(FischDrawFn drawFn);
void fischTransitionTo(FischDrawFn drawFn);

void drawFischConfirmOverlay(const char* question, int choice);
bool fischConfirmOverlay(const char* question);

void drawFischLoadingGifFrame(const uint8_t* frame);
void drawFischLoadingFrame();
void runFischLoading();

void fischPrintCentered(const String& text, int y);
String fischTruncateToFit(const String& text, int maxWidthPx);
String fischFormatCoins(uint32_t v);
String fischSizeLabel(float weightKg, float minKg, float maxKg);
void fischPrintNameCentered(const String& text, int topY);
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

int fischLogCategoryCount();
String fischLogCategoryName(int idx);
void fischBuildLogFilter(int category);
void drawFischLogFrame();
void runFischLogBrowseMenu();
void drawFischLogCategoryFrame();
void runFischLogMenu();

int fischCountItemsOfSpecies(int catalogIdx);
int fischCountOwnedSpecies();
int fischOwnedCatalogIndex(int ownedIdx);
int fischInventorySlotOfSpeciesItem(int catalogIdx, int itemIdx);
int fischInventoryFreeSlot();
bool fischInventoryIsFull();
bool fischAddFishToInventory(int catalogIdx, float weightKg, int mutationIndex);
void fischRemoveFishFromInventory(int invSlot);
uint32_t fischSellPriceFor(FischRarity rarity, float weightKg, int mutationIndex);
uint32_t fischTotalInventoryValue();
uint32_t fischSellAllInventory();
uint32_t fischSpeciesTotalValue(int catalogIdx);
uint32_t fischSellAllOfSpecies(int catalogIdx);

uint32_t fischXpForLevel(int level);
int fischLevelForXp(uint32_t xp);
int fischCurrentLevel();

bool fischOwnsRod(int rodIdx);
bool fischOwnsMap(int mapIdx);

bool fischWorldBitAt(int wx, int wy);
void drawFischTravelMapFrame();
void runFischTravelMapMenu();
void drawFischTravelDetailFrame();
void runFischTravelIslandDetail(int mapIdx);
void fischResolveEquippedRodStats();

void drawFischInventoryTopFrame();
void runFischInventoryMenu();

void drawFischFishListFrame();
void runFischFishListMenu(bool sellMode);

void drawFischItemBrowseFrame();
void runFischItemBrowseMenu(int catalogIdx, bool sellMode);

void drawFischRodBrowseFrame();
void runFischRodBrowseMenu(bool shopMode);
bool fischOwnsBait(int baitIdx);
void fischBuildBaitFilter(int mapFilter);
void drawFischBaitBrowseFrame();
void runFischBaitBrowseMenu(bool shopMode, int mapFilter);

void drawFischShopMenuFrame();
void runFischShopMenu();

void drawFischStatsFrame();
void runFischStatsPage();

void drawFischSettingsFrame();
void runFischSettingsMenu();
void drawFischDebugFrame();
void runFischDebugAdjustLevel();
void runFischDebugAdjustCoins();
void runFischDebugMenu();

void runFischGame();

// ---------------------------------------------------------------------------
// Persistence (Preferences namespace "fisch") - อ่านครั้งเดียวตอนเข้าเกมเก็บไว้ใน fischCache แล้วให้
// ทุกหน้าจออ่านจาก cache ตรงๆ (ไม่เปิด Preferences ใหม่ทุกเฟรม ช้าและไม่จำเป็น) เขียนกลับ NVS เฉพาะตอน
// ข้อมูลเปลี่ยนจริง (จับปลาได้/cast/reset) getBytesLength ไม่ตรง sizeof = ยังไม่เคยเซฟ (บูตแรก) หรือ
// schema เปลี่ยน -> เริ่มจากค่าว่างทั้งหมดอย่างปลอดภัย แทนที่จะอ่านขยะ
// ---------------------------------------------------------------------------
/// เคลียร์ fischCache เป็นสถานะ "เริ่มใหม่" ที่ถูกต้อง - memset(0) เฉยๆ ไม่พอเพราะ FischFishItem.catalogIndex
/// ที่เป็น 0 หมายถึง "มีปลาชนิดแรกอยู่ในช่องนั้น" (Tilapia) ไม่ใช่ "ช่องว่าง" (ช่องว่างจริงคือ -1) ต้องเซ็ต
/// เองทุกช่องหลัง memset - equippedRodIndex/rodOwned ปล่อยเป็น 0/false ได้เลยเพราะ 0 = Basic Rod ซึ่งถือว่า
/// มีติดตัวเสมออยู่แล้วไม่ต้องพึ่ง rodOwned[] (ดูคอมเมนต์ FischSaveData) equippedBaitIndex ก็เหมือนกัน ปล่อย
/// เป็น 0 จาก zero-init ได้เลย เพราะ Basic Bait (index 0) ตอนนี้ฟรี/มีติดตัวเสมอแล้ว (ดู fischOwnsBait -
/// ผู้เล่นใหม่เคยโดนบทลงโทษ "ไม่มีเหยื่อ" ตั้งแต่ตัวแรกทั้งที่ยังไม่รู้จักระบบนี้เลยด้วยซ้ำ แก้ให้เริ่มที่
/// baseline ปกติเสมอ ไม่ใช่โดนลบตั้งแต่ต้น)
void fischResetSaveDataToEmpty() {
  memset(&fischCache, 0, sizeof(FischSaveData));
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    fischCache.inventory[i].catalogIndex = -1;
    fischCache.inventory[i].mutationIndex = -1; // 0 จะหมายถึงกลายพันธุ์ "Ethereal" พอดี ต้อง -1 ชัดเจน
  }
}

void fischLoadSaveData() {
  Preferences prefs;
  prefs.begin("fisch", true);
  size_t len = prefs.getBytesLength("data");
  if (len == sizeof(FischSaveData)) {
    prefs.getBytes("data", &fischCache, sizeof(FischSaveData));
  } else {
    fischResetSaveDataToEmpty();
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
// Loading screen - เล่นแอนิเมชันปลาจาก gif_fisch_loading.h (วาดจาก Pic Mode Editor) แทนที่ข้อความ "FISCH"
// เดิม (ตอนนั้นตัดสินใจ text-only ไปก่อนเพราะยังไม่มีเวลาวาด ตอนนี้มีรูปจริงแล้วเลยใช้)
// ---------------------------------------------------------------------------
/// วาดปลาเฟรมเดียวของ GIF loading เต็มจอ 128x64 - แพ็คบิตแบบเดียวกับที่ Pic Mode Editor export ออกมา
/// (idx=y*128+x, byte=idx/8, bit=idx%8, LSB-first - ไม่ใช่รูปแบบ display.drawBitmap() มาตรฐานของ
/// Adafruit_GFX ซึ่งเป็น MSB-first แถมเรียงแถวคนละแบบ) เขียนแยกเป็นของตัวเองในไฟล์นี้ ไม่เรียก drawGifFrame
/// ของ others_menu.h เพราะไฟล์นั้น #include หลัง fisch_game.h ใน .ino หลัก - ฟังก์ชันในไฟล์ที่ include
/// ทีหลังเรียกจากไฟล์ที่ include ก่อนหน้าไม่ได้ (ดู comment หัวไฟล์นี้เรื่อง forward declaration) ต่างจาก
/// drawGifFrame ตรงที่ไม่เรียก display.display() เอง (ให้ผู้เรียกสั่งเองตาม convention ทั้งไฟล์นี้)
void drawFischLoadingGifFrame(const uint8_t* frame) {
  if (!displayReady) return;
  display.clearDisplay();
  for (int y = 0; y < SCREEN_HEIGHT; y++) {
    for (int x = 0; x < SCREEN_WIDTH; x++) {
      int idx = y * SCREEN_WIDTH + x;
      uint8_t b = pgm_read_byte(&frame[idx / 8]);
      if ((b >> (idx % 8)) & 1) {
        display.drawPixel(x, y, SSD1306_WHITE);
      }
    }
  }
}

void drawFischLoadingFrame() {
  if (!displayReady) return;
  drawFischLoadingGifFrame(FISCH_LOADING_FRAMES[fischLoadingFrameIndex]);
  display.setTextSize(1);
  if (fischLoadingReady) {
    fischPrintCentered("Press OK to continue", 56);
  } else {
    String loadingText = "Loading";
    for (int i = 0; i < fischLoadingDots; i++) loadingText += ".";
    fischPrintCentered(loadingText, 56); // ใต้ตัวปลา - ภาพเว้นพื้นที่ล่างจอไว้ให้พอดีอยู่แล้ว
  }
}

void runFischLoading() {
  fischLoadingReady = false;
  fischLoadingFrameIndex = 0;
  fischLoadingDots = 0;
  fischTransitionTo(drawFischLoadingFrame);

  while (btnOk.isPressed()) { delay(20); }
  btnOk.lastPressed = false;

  // เล่นแอนิเมชันต่อไปเรื่อยๆไม่หยุด (ไม่ freeze ค้างเฟรมเดียวตอนรอกด) คำนวณระยะเวลาขั้นต่ำก่อนโชว์
  // "Press OK" จากจำนวนเฟรม x ดีเลย์จริงของภาพเอง (ไม่ hardcode ตายตัว) เผื่อมีคนวาดภาพใหม่/เปลี่ยนจำนวน
  // เฟรมทีหลัง ระยะเวลาจะยังพอดีเสมอโดยไม่ต้องมาแก้เลขตรงนี้เพิ่ม - จุดไข่ปลาขยับคนละจังหวะ (ไวกว่า เดิม
  // 250ms) จากเฟรมปลา ไม่ผูกกับ FISCH_LOADING_FRAME_DELAY_MS
  unsigned long minReadyMs = (unsigned long)FISCH_LOADING_FRAME_COUNT * FISCH_LOADING_FRAME_DELAY_MS;
  const unsigned long DOT_TICK_MS = 250;
  unsigned long startT = millis();
  unsigned long lastGifTick = startT;
  unsigned long lastDotTick = startT;

  while (true) {
    unsigned long now = millis();
    if (!fischLoadingReady && now - startT >= minReadyMs) fischLoadingReady = true;

    bool needRedraw = false;
    if (now - lastGifTick >= FISCH_LOADING_FRAME_DELAY_MS) {
      lastGifTick = now;
      fischLoadingFrameIndex = (fischLoadingFrameIndex + 1) % FISCH_LOADING_FRAME_COUNT;
      needRedraw = true;
    }
    if (!fischLoadingReady && now - lastDotTick >= DOT_TICK_MS) {
      lastDotTick = now;
      fischLoadingDots = (fischLoadingDots + 1) % 4;
      needRedraw = true;
    }
    if (needRedraw) {
      drawFischLoadingFrame();
      display.display();
    }

    if (fischLoadingReady && btnOk.consumeJustPressed()) return;
    delay(10);
  }
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

/// ย่อตัวเลขเหรียญให้สั้นลง (1000 -> 1k, 2500000 -> 2.5M) - ราคาเบ็ดชุด endgame ขึ้นไปถึงหลักล้านแล้ว โชว์
/// เต็มๆ "Cost: 2500000" ยาวเกินจอ 128px ที่ text size 1 แน่นอน ตัดทศนิยมทิ้งถ้าลงตัวพอดี (200000 -> "200k"
/// ไม่ใช่ "200.0k") กันรก
String fischFormatCoins(uint32_t v) {
  float divided;
  const char* suffix;
  if (v >= 1000000UL) { divided = v / 1000000.0f; suffix = "M"; }
  else if (v >= 1000UL) { divided = v / 1000.0f; suffix = "k"; }
  else { return String(v); }
  int whole = (int)divided;
  int frac = (int)((divided - whole) * 10.0f + 0.5f);
  if (frac >= 10) { whole++; frac = 0; }
  String s = String(whole);
  if (frac != 0) { s += "."; s += String(frac); }
  s += suffix;
  return s;
}

/// ป้ายขนาดตามตำแหน่งน้ำหนักที่จับได้จริงเทียบกับช่วง min-max ของสปีชีส์นั้น (แบ่ง 4 ส่วนเท่าๆกัน) - แค่
/// ของแต่งเพิ่มความตื่นเต้นตอนได้ตัวใหญ่สุดขั้ว ไม่กระทบราคาขาย/กลไกอะไรเลย (งานวิจัยจาก Fisch จริง)
String fischSizeLabel(float weightKg, float minKg, float maxKg) {
  if (maxKg <= minKg) return "";
  float frac = (weightKg - minKg) / (maxKg - minKg);
  if (frac < 0.25f) return "Tiny";
  if (frac < 0.50f) return "Small";
  if (frac < 0.75f) return "Big";
  return "Giant";
}

/// วาดชื่อ (ปลา/เบ็ด ฯลฯ) กึ่งกลางแนวนอน - เคยลอง Picopixel (ฟอนต์เล็ก) แล้วก็เคยลอง marquee เลื่อนไปมา
/// มาก่อน แต่สุดท้ายตัดสินใจง่ายกว่า: ตั้งชื่อในตาราง (fish/rod catalog) ให้สั้นพอที่จะไม่มีตัวไหนเกินจอเลย
/// ตั้งแต่ต้น (เช็คแล้วว่าไม่เกิน ~20 ตัวอักษรที่ text size 1) เหลือ fischTruncateToFit ไว้เป็นแค่ตาข่ายกันพลาด
/// เฉยๆ (ไม่ควรถูกใช้จริงถ้าตั้งชื่อดีๆ)
void fischPrintNameCentered(const String& text, int topY) {
  if (!displayReady) return;
  display.setTextSize(1);
  fischPrintCentered(fischTruncateToFit(text, SCREEN_WIDTH - 4), topY);
}

void drawFischMainMenuFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  // ไม่โชว์เลเวลตรงนี้แล้ว (ผู้ใช้ขอให้กลับไปเป็นแบบเดิม) ดูเลเวลได้จากหน้า Stats อย่างเดียว
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
      if (fischConfirmOverlay("Exit Fisch?")) {
        Serial.println("[Fisch] ออกกลับเมนู Others");
        fischIrisClose(); // fade ปิดดำทั้งจอก่อน
        delay(1000);      // ค้างจอดำไว้สักพักก่อนคืนคอนโทรลกลับ runOthersMenu (ซึ่งจะวาดเมนู Others ทับทันที)
        return;
      }
      drawFischMainMenuFrame();
      display.display();
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }
    if (btnOk.consumeJustPressed()) {
      if (fischMenuIndex == 0) {
        if (fischInventoryIsFull()) {
          // เตือนตั้งแต่ตรงนี้เลย กันเสียเวลาเข้าไปตกทั้งกระบวนการแล้วมาเจอว่าใส่กระเป๋าไม่ได้ตอนจบ
          fischTransitionTo([]() {
            if (!displayReady) return;
            display.clearDisplay();
            display.setTextSize(1);
            fischPrintCentered("Inventory full!", 20);
            fischPrintCentered("Sell some fish", 32);
            fischPrintCentered("at the Shop first", 44);
          });
          delay(1600);
          fischTransitionTo(drawFischMainMenuFrame);
        } else {
          runFischFishingSequence();
        }
      } else if (fischMenuIndex == 1) {
        runFischTravelMapMenu();
      } else if (fischMenuIndex == 2) {
        runFischLogMenu();
      } else if (fischMenuIndex == 3) {
        runFischInventoryMenu();
      } else if (fischMenuIndex == 4) {
        runFischShopMenu();
      } else if (fischMenuIndex == 5) {
        runFischStatsPage();
      } else if (fischMenuIndex == 6) {
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
//
// ปรับใหม่ (เดิม roll 0-99 + bonus 0-70 เจอ Epic/Legendary บ่อยเกินไปจนเงิน/XP ท่วม - ผู้ใช้ชี้ว่าต้นตอคือ
// "โอกาส" ไม่ใช่แค่ราคาขายปลายทาง) เปลี่ยนสเกล roll เป็น 0-999 ละเอียดขึ้น แล้วขยับ threshold ของหายากขึ้นสูง
//
// ผ่อนลงอีกรอบ (เล่นจริง 2 ชม.ได้แค่ lvl 16 - แก้ 2 เรื่องพร้อมกัน (curve เลเวล+โอกาส) แรงไปหน่อยตอนรวมกัน)
// threshold ขยับลงนิดหน่อยจากรอบก่อน ไม่กลับไปเท่าเดิม: cast สมบูรณ์แบบ+เบ็ดโชคดีสุด โอกาส Legendary ราวๆ
// 22% ส่วน cast ธรรมดา+เบ็ดพื้นฐาน Legendary 1.5% Epic 4.5% Rare 12% Uncommon 27%
// ---------------------------------------------------------------------------
FischRarity fischRollRarity(int castQuality) {
  if (fischDebugForceRarity >= 0) return (FischRarity)fischDebugForceRarity; // Debug Mode > Force Rarity
  int bonus = castQuality + fischActiveLuckBonus * 4; // เบ็ดที่ใส่อยู่ (ดู fischResolveEquippedRodStats)
  if (bonus < 0) bonus = 0;                                 // เบ็ดโชคร้ายกดโบนัสลงได้ แต่ไม่ต่ำกว่า 0
  int roll = random(0, 1000) + bonus;
  FischRarity result;
  if (roll >= 985) result = FISCH_LEGENDARY;
  else if (roll >= 940) result = FISCH_EPIC;
  else if (roll >= 820) result = FISCH_RARE;
  else if (roll >= 550) result = FISCH_UNCOMMON;
  else result = FISCH_COMMON;

  // Sentinel Keeper Rod เท่านั้น (pity timer - ดู FISCH_ROD_IDX_SENTINEL_KEEPER) นับตั้งแต่ได้ Rare ขึ้นไป
  // ครั้งล่าสุด ครบ 8 ครั้งแล้วยังไม่เคยได้เลยบังคับเป็น Rare ทันที (การันตีอย่างน้อย Rare ทุกๆ 8 ครั้ง)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_SENTINEL_KEEPER) {
    if (result >= FISCH_RARE) {
      fischSentinelPityCount = 0;
    } else {
      fischSentinelPityCount++;
      if (fischSentinelPityCount >= 8) {
        result = FISCH_RARE;
        fischSentinelPityCount = 0;
      }
    }
  }
  return result;
}

int fischPickFishForRarity(FischRarity r) {
  // Wormhole (บางเบ็ดเท่านั้น - ดู FISCH_MAPS ด้านบนสำหรับที่มาไอเดีย/FischRodEntry.wormholeChancePercent)
  // มีโอกาสสุ่มเลือก "เกาะเป้าหมาย" เป็นเกาะอื่นแทนเกาะที่อยู่จริงตอนนี้ - ปลาทั่วไป (-1) ยังหาเจอได้ปกติเสมอ
  // ไม่ว่ากรณีไหน มีผลแค่ปลาเฉพาะเกาะเท่านั้นว่าจะเปิดให้เกาะไหน
  int targetMapIndex = fischCache.currentMapIndex;
  if (fischActiveWormholeChancePercent > 0 && FISCH_MAP_COUNT > 1 &&
      random(0, 100) < fischActiveWormholeChancePercent) {
    int otherMap = random(0, FISCH_MAP_COUNT - 1);
    if (otherMap >= fischCache.currentMapIndex) otherMap++; // เลี่ยงเกาะปัจจุบัน (ไม่งั้นไม่ใช่ "ข้ามเกาะ" จริง)
    targetMapIndex = otherMap;
  }

  int candidates[FISCH_CATALOG_COUNT];
  int count = 0;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    if (FISCH_CATALOG[i].rarity != r) continue;
    // exclusiveMapIndex -1 = หาได้ทุกเกาะ, >=0 = หาได้เฉพาะเกาะนั้น (ต้องตรงกับ targetMapIndex)
    int8_t excl = FISCH_CATALOG[i].exclusiveMapIndex;
    if (excl != -1 && excl != targetMapIndex) continue;
    candidates[count++] = i;
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

  // สุ่มน้ำหนักตรงนี้เลย (ย้ายมาจากหลัง Reel สำเร็จ) เพราะตอนนี้ Reel ต้องรู้น้ำหนักปลาล่วงหน้าแล้วว่าเกิน
  // maxWeightKg ของเบ็ดที่ใส่อยู่หรือเปล่า (ดู fischReelOverloaded ใน runFischReelPhase) - ค่าที่สุ่มได้ตรงนี้
  // คือค่าจริงที่จะได้ถ้าจับสำเร็จ ไม่สุ่มซ้ำอีกทีตอนจบ
  const FischFishEntry& fEntry = FISCH_CATALOG[fischCurrentFishIndex];
  float wRange = fEntry.maxWeightKg - fEntry.minWeightKg;
  float wFrac = random(0, 10001) / 10000.0f;
  // Drift Net Rod เท่านั้น (ดู FISCH_ROD_IDX_DRIFT_NET) - เอียงน้ำหนักไปทาง max เสมอ (sqrt ดัน distribution
  // ไปทางค่าสูงกว่าเดิม แทนที่จะสุ่มเรียบๆ 0-1 ตรงๆ)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_DRIFT_NET) wFrac = sqrtf(wFrac);
  fischCaughtWeight = fEntry.minWeightKg + wFrac * wRange;

  long waitMs = random(2200, 4500) - (castQuality * 18L);
  waitMs -= fischActiveLureSpeedBonus * 15L; // Lure Speed ของเบ็ด (ดู fischResolveEquippedRodStats) ลดเวลารอกัดเพิ่ม
  // Anchor Rod เท่านั้น (ดู FISCH_ROD_IDX_ANCHOR) - เวลารอกัดลดลงครึ่งหนึ่งจากที่คำนวณได้ (ทำก่อนหัก floor)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_ANCHOR) waitMs /= 2;
  if (waitMs < 600) waitMs = 600;

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
      // เหมือน bug ที่เจอใน Reel: ไม่ reset startT ตรงนี้ เวลาที่ค้างอยู่หน้า dialog จะไปนับรวมเป็นเวลารอ
      // กัดด้วย ทำให้พอกลับมาเล่นต่อ อาจจะรู้สึกว่ารอไม่ทันไรปลาก็กัดเลย (หรือแย่กว่านั้นคือ waitMs หมด
      // ไปแล้วตั้งแต่ตอนค้างอยู่หน้า dialog) - reset ให้เริ่มนับเวลารอใหม่หลังกลับมาเล่นต่อเสมอ
      startT = millis();
      lastTick = startT;
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
  fischHookMissesAllowed = fischHookHitsRequired / 5 + fischActiveHookMissBonus; // ผลของเบ็ดที่ใส่อยู่
  if (fischHookMissesAllowed < 0) fischHookMissesAllowed = 0;
  fischHookMissCount = 0;
  long windowMs = 900 - (diff * 5L) + fischActiveHookMsBonus; // ผลของเบ็ดที่ใส่อยู่
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
  // เบ็ดรับน้ำหนักปลาตัวนี้ไม่ไหว - เตือนไว้ในหัวข้อเลย (ดู fischReelOverloaded/FischRodEntry.maxWeightKg)
  display.print(fischReelOverloaded ? "Reeling! HEAVY" : "Reeling...");

  int barX = 10, barY = 16, barW = 108, barH = 8;
  display.drawRect(barX, barY, barW, barH, SSD1306_WHITE);

  int fishX = barX + (int)(fischFishPos * barW);
  display.fillRect(fishX - 2, barY - 4, 4, 4, SSD1306_WHITE); // ตำแหน่งปลา (เหนือแถบ ไม่ชนกับแถบจับ)

  // clamp ให้อยู่ในกรอบแถบเสมอ - fischCaptureHalfWidth ปรับได้กว้างถึง 0.35 (ดูเบ็ด controlBonus สุดขั้ว
  // อย่าง So Heavy Rod) รวมกับ fischCapturePos ที่ชิดขอบ 0/1 ได้ ถ้าไม่ clamp ตรงนี้แถบจับจะวาดล้นออกนอก
  // กรอบ drawRect ด้านบนไปเลย (เคยเป็นแบบนี้มานานแล้ว ดูน่ารำคาญตา) เป็นแค่การ clamp ตอนวาดเท่านั้น ไม่กระทบ
  // ตำแหน่ง/ค่าจริงที่ใช้เช็ค overlap เลย
  int capLeft = barX + (int)((fischCapturePos - fischCaptureHalfWidth) * barW);
  int capRight = barX + (int)((fischCapturePos + fischCaptureHalfWidth) * barW);
  if (capLeft < barX) capLeft = barX;
  if (capRight > barX + barW) capRight = barX + barW;
  int capW = capRight - capLeft;
  if (capW > 0) display.fillRect(capLeft, barY, capW, barH, SSD1306_WHITE);

  // Progress
  display.drawRect(barX, 28, barW, 5, SSD1306_WHITE);
  int pw = (int)(fischReelProgress / 100.0f * (barW - 2));
  display.fillRect(barX + 1, 29, pw, 3, SSD1306_WHITE);

  // Tension (เบ็ดขาด) - แยกจาก progress คนละความหมาย: อันนี้วัดว่ากด OK ค้างหนักไปหรือเปล่า เต็ม 100 =
  // เบ็ดขาดทันทีไม่ว่า progress จะไปถึงไหนแล้ว - เส้นบางเส้นเดียวแบบเดียวกับแถบเวลาใน Hook reaction
  // (ยาวขึ้นตามค่า) แทนกล่องหนาแบบเดิม ตามที่ผู้ใช้บอกว่ากล่องเดิมหนาไปหน่อย
  int tw = (int)(fischReelTension / 100.0f * barW);
  display.drawFastHLine(barX, 38, tw, SSD1306_WHITE);

  // Surge warning - กระพริบเตือนช่วงสั้นๆก่อนปลาจะกระตุกแรง (เหมือนไฟแดงใน Fisch จริง) ปล่อย OK ทันเวลา
  // จะไม่โดน tension พุ่ง - กระพริบด้วย (millis()/120)%2 ให้เห็นชัดว่ากำลังเตือนอยู่ ไม่ใช่ข้อความค้างนิ่ง
  if (fischSurgeWarningActive && (millis() / 120) % 2 == 0) {
    fischPrintCentered("SURGE! Release!", 48);
  } else if (fischReelPrecisionFrozen) {
    fischPrintCentered("FROZEN!", 48); // Angler's Rod (Precision Strike) - ดู FISCH_ROD_IDX_ANGLERS
  } else if (fischCache.equippedRodIndex == FISCH_ROD_IDX_HALO) {
    // Halo Rod เท่านั้น (ดู FISCH_ROD_IDX_HALO) - โชว์ชื่อปลาระหว่าง Reel เลย ปกติทั้งไฟล์นี้ตั้งใจไม่บอก
    // ชื่อจนกว่าจะถึงหน้า Result กันสปอยล์ (ดูคอมเมนต์หัวฟังก์ชันนี้) เบ็ดตัวนี้ทำลายกฎนั้นโดยตั้งใจเป็นความ
    // สามารถพิเศษของมันเอง
    fischPrintCentered(fischTruncateToFit(FISCH_CATALOG[fischCurrentFishIndex].name, SCREEN_WIDTH - 4), 48);
  }

  display.setCursor(0, 56);
  display.print("BACK = quit");
}

bool runFischReelPhase() {
  fischFishPos = 0.5f;
  fischFishVel = 0;
  fischCapturePos = 0.5f;
  fischCaptureVel = 0;
  fischReelProgress = 0;
  fischReelOverlapSinceMs = 0;
  fischReelTension = 0;
  fischSurgeWarningActive = false;
  fischNextSurgeAtMs = millis() + random(FISCH_SURGE_MIN_INTERVAL_MS, FISCH_SURGE_MAX_INTERVAL_MS + 1);
  fischPrecisionFreezeUntilMs = 0;
  fischReelPrecisionFrozen = false;
  fischNextPrecisionStrikeAtMs = millis() + FISCH_PRECISION_STRIKE_INTERVAL_MS;
  fischSecondWindUsed = false;   // Hollow Echo Rod - ต่อทริป
  fischTensionResetUsed = false; // Kettle Forged Rod - ต่อทริป
  fischSimmerHoldSinceMs = 0;    // Simmer Rod - ต่อทริป
  fischNextTidalPushAtMs = millis() + FISCH_TIDAL_PUSH_INTERVAL_MS; // Tidal Push Rod - ต่อทริป

  // Perfect/Well Catch (ดู fischCaughtPerfect/fischCaughtWell) - track เป็น local ในนี้เลยเพราะไม่มีใครอื่น
  // ต้องอ่านระหว่างทาง เขียนลง global ตอนจบแค่ทีเดียวก่อน return true
  bool everDecreased = false;
  bool reachedHalf = false;
  float minProgressSinceHalf = 999.0f;

  // รู้น้ำหนักปลาล่วงหน้าแล้ว (สุ่มไว้ตั้งแต่ runFischBiteWaitPhase) เช็คตรงนี้ครั้งเดียวว่าเกินเบ็ดไหวไหม
  fischReelOverloaded = (fischActiveMaxWeightKg > 0 && fischCaughtWeight > fischActiveMaxWeightKg);

  // ทุกค่าด้านล่างสเกลตาม reelDifficulty (0..100) ของปลาตัวนี้โดยเฉพาะ - นี่แหละคือจุดที่ทำให้แต่ละชนิด
  // "พิเศษ" กว่ากันจริงๆ ไม่ใช่แค่สุ่มเฉยๆ: ปลายากยิ่งขยับเร็ว/กระตุกแรงกว่า, แถบจับแคบกว่า, ตอนทับแล้ว
  // progress ขึ้นช้ากว่า, แต่พอหลุดร่วงเร็วกว่า - รวมกันแล้วปลา rarity สูงเล่นยากขึ้นจริงในทุกมิติพร้อมกัน
  // diff ดิบ (ไม่ถูกเบ็ดปรับ) ใช้เป็น baseline ของแถบจับ/อัตรา progress/tension - Control (controlBonus) กับ
  // progressRateBonus ปรับสองอย่างนั้นตรงๆแยกกันคนละทาง ส่วน Resilience (resilienceBonus) แยกไปคำนวณ
  // "moveDiff" ต่างหากด้านล่าง มีผลแค่ความเร็ว/ความกระตุกของปลาเท่านั้น ไม่ทับซ้อนกับอีกสองตัวอีกต่อไป
  int diff = FISCH_CATALOG[fischCurrentFishIndex].reelDifficulty; // 0..100 (ในคลังจริงอยู่ราว 15..95)

  fischCaptureHalfWidth = 0.20f - (diff / 100.0f) * 0.14f; // ง่าย 0.20 (กว้าง) -> ยาก 0.06 (แคบมาก)
  fischCaptureHalfWidth += fischActiveControlBonus / 100.0f; // ผลตรงๆของเบ็ด (Control)
  if (fischCaptureHalfWidth < 0.03f) fischCaptureHalfWidth = 0.03f;
  if (fischCaptureHalfWidth > 0.35f) fischCaptureHalfWidth = 0.35f; // กันแถบกว้างจนไม่มีความหมายจะเล็ง

  float rateIn = 34.0f - (diff / 100.0f) * 18.0f;   // ง่าย +34/วิ ตอนทับ -> ยาก +16/วิ (ขึ้นช้ากว่ามาก)
  rateIn += fischActiveProgressRateBonus; // ผลของเบ็ดที่ใส่อยู่ - ไม่ clamp ไว้เลย (เบ็ดขำๆ ±900 ตั้งใจให้
                                           // เละแบบนั้นจริงๆ ดูคอมเมนต์ FischRodEntry.progressRateBonus)
  // Ebbtide Rod เท่านั้น (ดู FISCH_ROD_IDX_EBBTIDE) - ปลาหลุดไปครั้งก่อน (ตั้ง fischTenacityActive ไว้ใน
  // runFischFishingSequence ตอนหลุด) รอบถัดไป rateIn +30% แค่ครั้งเดียวแล้วเคลียร์ทิ้ง (ไม่ใช่ทุกรอบตลอดไป)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_EBBTIDE && fischTenacityActive) {
    rateIn *= 1.3f;
    fischTenacityActive = false;
  }
  // Acorn Guardian Rod เท่านั้น (Berserk - ดู FISCH_ROD_IDX_ACORN_GUARDIAN) - ปลายิ่งยาก (diff สูง) ยิ่งได้
  // โบนัส rateIn เยอะขึ้น ตรงข้ามกับเบ็ดทั่วไปที่ยากขึ้นแล้ว rateIn ลดลงเฉยๆ
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_ACORN_GUARDIAN) {
    rateIn += diff * FISCH_ACORN_BERSERK_PER_DIFF;
  }
  float rateOut = 10.0f + (diff / 100.0f) * 22.0f;  // ง่าย -10/วิ ตอนหลุด -> ยาก -32/วิ (ร่วงเร็วกว่ามาก)

  // moveDiff = diff หลังหัก Resilience ของเบ็ด (ติดลบได้) ใช้แค่คำนวณความเร็ว/ความกระตุกของปลาสามตัวด้านล่าง
  // เท่านั้น ไม่กระทบแถบจับ/อัตรา progress/tension เลย (ต่างจาก reelEaseBonus เดิมที่ปนกันหมด)
  int moveDiff = diff - fischActiveResilienceBonus;
  if (moveDiff < 0) moveDiff = 0;
  if (moveDiff > 100) moveDiff = 100;

  float fishSpeed = 0.10f + (moveDiff / 100.0f) * 0.55f; // ง่าย ~0.10 -> ยาก ~0.65 (กว้างกว่าเดิมชัดเจน)
  unsigned long fishRetargetIntervalMs = 650 - (moveDiff * 5UL); // ยากยิ่งเปลี่ยนทิศถี่ขึ้น
  if (fishRetargetIntervalMs < 120) fishRetargetIntervalMs = 120;
  int jerkChancePercent = (moveDiff * 40) / 100; // โอกาส "กระตุกแรง" ต่อรอบเปลี่ยนทิศ สูงสุด 40% ตอน moveDiff=100

  // เคยลองให้ Common/Uncommon ขยับเป็นคลื่นทายทางได้ (patterned) มาก่อน แต่พอเล่นจริงเองแล้วรู้สึกว่าแบบ
  // สุ่ม/กระตุกเดิมง่ายกว่าเยอะ (แม้จะทายทางไม่ได้ก็ตาม) เลยถอยกลับมาใช้ chaotic ทุก tier เหมือนเดิม

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
      // บั๊กที่เจอจริง: ถ้าไม่ reset lastTick/lastRetarget ตรงนี้ เวลาที่ค้างอยู่หน้า dialog (ผู้เล่นคิด
      // นานแค่ไหนก็ได้) จะไปนับรวมเป็น dt ก้อนใหญ่ในติ๊กแรกหลังกลับมาเล่นต่อ - ถ้าตอนนั้นแถบจับทับปลา
      // พอดี progress += rateIn * dt (dt เป็นวินาทีที่ค้างอยู่หน้า dialog) จะพุ่งเกิน 100 ในติ๊กเดียวทันที
      // (เหมือนโดนโกงเข้าฟรี) ต้อง reset ให้ dt ก้อนถัดไปเป็นค่าปกติ ไม่ใช่ผลรวมเวลาที่หยุดไป
      lastTick = millis();
      lastRetarget = millis();
      // เหตุผลเดียวกับด้านบน - เลื่อน surge รอบถัดไปออกไปด้วย ไม่งั้นอาจโดน surge ทันทีที่กลับมาเล่นต่อ
      // (หรือแย่กว่านั้นคือพลาดโดนไปแล้วระหว่างที่ค้างอยู่หน้า dialog โดยไม่รู้ตัวเลย)
      fischSurgeWarningActive = false;
      fischNextSurgeAtMs = millis() + random(FISCH_SURGE_MIN_INTERVAL_MS, FISCH_SURGE_MAX_INTERVAL_MS + 1);
      // เหตุผลเดียวกันอีกที - ถ้าไม่ reset ตัวนี้ด้วย เวลาที่ค้างอยู่หน้า dialog จะถูกนับเป็น dwell ที่ผ่าน
      // ไปแล้วของ Near Impossible Rod ทำให้ progress ขึ้นทันทีตอนกลับมาเล่นต่อทั้งที่ยังไม่ได้ประคองจริง
      fischReelOverlapSinceMs = 0;
      // เหตุผลเดียวกันอีกที - เลื่อน Precision Strike รอบถัดไปออกไปด้วย (Angler's Rod)
      fischPrecisionFreezeUntilMs = 0;
      fischReelPrecisionFrozen = false;
      fischNextPrecisionStrikeAtMs = millis() + FISCH_PRECISION_STRIKE_INTERVAL_MS;
      drawFischReelFrame();
      display.display();
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }

    unsigned long now = millis();

    // Precision Strike (Angler's Rod เท่านั้น) - แช่แข็งปลาให้นิ่งเป็นจังหวะสม่ำเสมอทุก
    // FISCH_PRECISION_STRIKE_INTERVAL_MS นาน FISCH_PRECISION_STRIKE_FREEZE_MS ต่อครั้ง ไม่ใช่สุ่มแบบ surge
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_ANGLERS && now >= fischNextPrecisionStrikeAtMs) {
      fischPrecisionFreezeUntilMs = now + FISCH_PRECISION_STRIKE_FREEZE_MS;
      fischNextPrecisionStrikeAtMs = now + FISCH_PRECISION_STRIKE_INTERVAL_MS;
      fischFishVel = 0; // หยุดนิ่งทันทีตอนเริ่มแช่แข็ง ไม่ใช่แค่เลิกเปลี่ยนทิศ
    }
    fischReelPrecisionFrozen = (now < fischPrecisionFreezeUntilMs);

    if (!fischReelPrecisionFrozen && now - lastRetarget >= fishRetargetIntervalMs) {
      lastRetarget = now;
      bool jerk = random(0, 100) < jerkChancePercent; // ปลายากมีโอกาสพุ่งแรงกว่าปกติเป็นครั้งคราว
      float mag = jerk ? fishSpeed * 1.8f : fishSpeed;
      fischFishVel = ((random(0, 200) / 100.0f) - 1.0f) * mag; // -mag..+mag

      // Ring Piercer Rod เท่านั้น (Stab - ดู FISCH_ROD_IDX_RING_PIERCER) ทุกครั้งที่ปลาเปลี่ยนทิศ มีโอกาส
      // "แทง" ได้ progress ก้อนใหญ่ทันที + ทำปลานิ่งชั่วครู่ (เหมือน Chaotic/Piercing enchant ของจริง)
      if (fischCache.equippedRodIndex == FISCH_ROD_IDX_RING_PIERCER && random(0, 100) < FISCH_RING_PIERCER_STAB_CHANCE) {
        fischReelProgress += FISCH_RING_PIERCER_STAB_PROGRESS;
        if (fischReelProgress > 100) fischReelProgress = 100;
        fischFishVel = 0;
        playBeep(20);
      }
    }

    // Tidal Push Rod เท่านั้น (ดู FISCH_ROD_IDX_TIDAL_PUSH) - ดันแถบจับเข้าใกล้ปลาอัตโนมัติเป็นจังหวะสม่ำเสมอ
    // (คนละแบบกับ Precision Strike ที่แช่แข็งปลา - อันนี้ช่วยที่ตัวแถบจับแทน)
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_TIDAL_PUSH && now >= fischNextTidalPushAtMs) {
      fischCapturePos += (fischFishPos - fischCapturePos) * FISCH_TIDAL_PUSH_FRACTION;
      fischNextTidalPushAtMs = now + FISCH_TIDAL_PUSH_INTERVAL_MS;
    }

    if (now - lastTick >= FISCH_REEL_TICK_MS) {
      float dt = (now - lastTick) / 1000.0f;
      lastTick = now;

      // เด้งแบบมี damping ตอนชนขอบถ้าเปิด Momentum ไว้ (Settings > Reel Momentum) ปิดไว้ = เด้งเต็มแรงแบบ
      // เดิมก่อนหน้านี้ (fischMomentumEnabled=false ทำให้ damping กลายเป็น 1.0 คือไม่หน่วงเลย) - ข้ามตอนแช่
      // แข็งอยู่ (precisionFrozen) ปลาต้องนิ่งสนิทจริงๆ ไม่ขยับแม้แต่จากแรงเฉื่อยเดิม
      if (!fischReelPrecisionFrozen) {
        float bounceDamping = fischMomentumEnabled ? FISCH_BOUNCE_DAMPING : 1.0f;
        fischFishPos += fischFishVel * dt;
        if (fischFishPos < 0) { fischFishPos = 0; fischFishVel = -fischFishVel * bounceDamping; }
        if (fischFishPos > 1) { fischFishPos = 1; fischFishVel = -fischFishVel * bounceDamping; }
      }

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

      // Tension (เบ็ดขาด) - กด OK ค้างดึงเบ็ด tension ขึ้นเรื่อยๆ (ยิ่งปลายากยิ่งขึ้นเร็ว) ปล่อยแล้วลดเร็ว
      // กว่าที่ขึ้น เป็นระยะๆปลาจะ "สำแดง" เตือนล่วงหน้าสั้นๆ - ถ้ายังกด OK ค้างอยู่ตอนนั้น tension พุ่งแรง
      // เป็นพิเศษ ถ้าปล่อยทันเวลา (อ่านจังหวะถูก) ไม่โดนพุ่ง แถมได้ progress โบนัสนิดหน่อยด้วย
      float tensionRiseRate = 12.0f + (diff / 100.0f) * 18.0f;
      if (holding) {
        fischReelTension += tensionRiseRate * dt;
      } else {
        fischReelTension -= 28.0f * dt;
      }
      if (fischReelTension < 0) fischReelTension = 0;

      // เบ็ดรับน้ำหนักปลาตัวนี้ไม่ไหว (ดู FischRodEntry.maxWeightKg) - tension ขึ้นเพิ่มอีกทางตลอดเวลา ไม่ว่า
      // จะกด OK ค้างอยู่หรือเปล่า (สายเครียดจากน้ำหนักปลาตรงๆ ไม่เกี่ยวกับที่ผู้เล่นทำ) ยิ่งหนักเกินยิ่งแรง -
      // Barrier Reef Rod / Abyssal Voyager Rod เท่านั้นที่ภูมิคุ้มกัน (ดู FISCH_ROD_IDX_BARRIER_REEF/_ABYSSAL_VOYAGER)
      bool overloadImmune = (fischCache.equippedRodIndex == FISCH_ROD_IDX_BARRIER_REEF ||
                              fischCache.equippedRodIndex == FISCH_ROD_IDX_ABYSSAL_VOYAGER);
      if (fischReelOverloaded && !overloadImmune) {
        float excessFrac = (fischCaughtWeight / fischActiveMaxWeightKg) - 1.0f;
        float extra = excessFrac * FISCH_OVERLOAD_TENSION_PER_EXCESS;
        if (extra > FISCH_OVERLOAD_TENSION_CAP) extra = FISCH_OVERLOAD_TENSION_CAP;
        fischReelTension += extra * dt;
      }

      fischSurgeWarningActive = (now + FISCH_SURGE_WARNING_LEAD_MS >= fischNextSurgeAtMs) && (now < fischNextSurgeAtMs);
      if (now >= fischNextSurgeAtMs) {
        if (holding) {
          fischReelTension += FISCH_SURGE_PENALTY;
          playBeep(30);
        } else {
          // Twitchy Rod เท่านั้น (ดู FISCH_ROD_IDX_TWITCHY) - ไวต่อจังหวะเป็นพิเศษ อ่านจังหวะ surge ถูกได้
          // โบนัสมากกว่าเบ็ดทั่วไปถึง 3 เท่า
          float safeBonus = FISCH_SURGE_SAFE_BONUS;
          if (fischCache.equippedRodIndex == FISCH_ROD_IDX_TWITCHY) safeBonus *= FISCH_TWITCHY_SURGE_BONUS_MULT;
          fischReelProgress += safeBonus;
        }
        fischNextSurgeAtMs = now + random(FISCH_SURGE_MIN_INTERVAL_MS, FISCH_SURGE_MAX_INTERVAL_MS + 1);
      }
      if (fischReelTension > 100) fischReelTension = 100;

      float progressBeforeTick = fischReelProgress; // ไว้เทียบหลัง tick นี้ (ดู Perfect/Well Catch ด้านล่าง)
      bool overlap = fabs(fischCapturePos - fischFishPos) <= fischCaptureHalfWidth;

      if (fischCache.equippedRodIndex == FISCH_ROD_IDX_SO_HEAVY) {
        // ก้อนหนักแต่ "อึด" - progress ขึ้นทีละนิดคงที่ตลอดเวลา ไม่สนตำแหน่งแถบจับเลย (ไม่มีทางร่วงเป็น 0)
        // กลยุทธ์ที่ดีที่สุดคือปล่อยรอเฉยๆไม่ต้องแตะ OK เลยด้วยซ้ำ (กด OK มีแต่เสี่ยง tension ขึ้นฟรีๆ)
        fischReelProgress += FISCH_SO_HEAVY_FLAT_RATE * dt;
      } else if (fischCache.equippedRodIndex == FISCH_ROD_IDX_NEAR_IMPOSSIBLE) {
        // แถบจับจิ๋วมาก ต้องประคองทับปลาต่อเนื่องให้ผ่าน dwell ก่อน progress ถึงจะขึ้น (เบ็ดอื่นขึ้นทันทีที่
        // ทับ) แต่พอหลุดจากปลา progress ร่วงช้ากว่าเบ็ดทั่วไปชดเชยให้
        if (overlap) {
          if (fischReelOverlapSinceMs == 0) fischReelOverlapSinceMs = now;
          if (now - fischReelOverlapSinceMs >= FISCH_NEAR_IMPOSSIBLE_DWELL_MS) {
            fischReelProgress += rateIn * dt;
          }
        } else {
          fischReelOverlapSinceMs = 0;
          fischReelProgress -= rateOut * FISCH_NEAR_IMPOSSIBLE_LOSS_MULT * dt;
        }
      } else if (fischCache.equippedRodIndex == FISCH_ROD_IDX_SIMMER) {
        // Simmer Rod - ยิ่งทับปลาต่อเนื่องนาน rateIn ยิ่งเพิ่ม ("อุ่นเครื่อง") หลุดจากปลาเมื่อไหร่รีเซ็ตกลับศูนย์
        if (overlap) {
          if (fischSimmerHoldSinceMs == 0) fischSimmerHoldSinceMs = now;
          float heldSec = (now - fischSimmerHoldSinceMs) / 1000.0f;
          float rampBonus = heldSec * FISCH_SIMMER_RAMP_PER_SEC;
          if (rampBonus > FISCH_SIMMER_RAMP_CAP) rampBonus = FISCH_SIMMER_RAMP_CAP;
          fischReelProgress += (rateIn + rampBonus) * dt;
        } else {
          fischSimmerHoldSinceMs = 0;
          fischReelProgress -= rateOut * dt;
        }
      } else if (overlap) {
        fischReelProgress += rateIn * dt;
      } else {
        fischReelProgress -= rateOut * dt;
      }
      // Tidecaller Rod เท่านั้น (ดู FISCH_ROD_IDX_TIDECALLER) - progress ไหลเพิ่มอีกนิดเสมอไม่ว่าทับปลาหรือ
      // เปล่า บวกทับจากผลของ branch ด้านบนอีกที (ไม่ใช่แทนที่)
      if (fischCache.equippedRodIndex == FISCH_ROD_IDX_TIDECALLER) {
        fischReelProgress += FISCH_TIDECALLER_FLAT_RATE * dt;
      }
      // เช็ค "ค้างที่ 0" ตรงนี้จุดเดียวไม่ว่าเข้าทางไหน (เดิมเช็คแค่ตอนไม่ overlap - พอมีเบ็ดที่ rateIn
      // ติดลบได้ progress ร่วงได้แม้ตอน overlap เองด้วย ถ้าไม่ย้ายมาเช็ครวมตรงนี้จะกลายเป็นค้างลบไม่รู้จบ
      // ไม่มีทางหลุดเลยสักทาง) - เดิมมีตัวจับเวลาแยกต่างหาก "ค้างเกิน 3 วิ = หลุดทันที" คนละระบบกับ tension
      // แต่ผู้ใช้บอกว่ามันคนละเรื่องกันไปเปล่าๆ เลยยุบรวมให้เหลือทางเดียว: ค้างที่ 0 ตอนไหน tension ขึ้นเรื่อยๆ
      // ตอนนั้นเลย (ไม่มีช่วงรอ/เดเลย์อีกต่อไป) จบเกมด้วย tension เต็ม 100 เป็นเงื่อนไขเดียวเท่านั้น
      if (fischReelProgress <= 0) {
        fischReelProgress = 0;
        // Hollow Echo Rod เท่านั้น (Second Wind - ดู FISCH_ROD_IDX_HOLLOW_ECHO) - progress จะตาย 0 ครั้งแรก
        // ของทริปนี้ กู้กลับมา 50% แทนแล้วเล่นต่อได้เลย (ใช้ได้แค่ครั้งเดียว/ทริป) ไม่โดน tension เพิ่มรอบนี้ด้วย
        if (fischCache.equippedRodIndex == FISCH_ROD_IDX_HOLLOW_ECHO && !fischSecondWindUsed) {
          fischSecondWindUsed = true;
          fischReelProgress = FISCH_HOLLOW_ECHO_RECOVER_TO;
          playBeepPattern(2, 40, 40);
        } else if (fischCache.equippedRodIndex != FISCH_ROD_IDX_PATIENT &&
                   fischCache.equippedRodIndex != FISCH_ROD_IDX_ABYSSAL_VOYAGER) {
          // Patient Rod / Abyssal Voyager Rod เท่านั้นที่ค้างที่ 0 นานแค่ไหนก็ไม่โดน tension เพิ่ม เบ็ดอื่น
          // โดนตามปกติ (ดู FISCH_ROD_IDX_PATIENT/_ABYSSAL_VOYAGER)
          fischReelTension += FISCH_ZERO_TENSION_RISE_RATE * dt;
          if (fischReelTension > 100) fischReelTension = 100;
        }
      }
      if (fischReelProgress > 100) fischReelProgress = 100;

      // Perfect Catch = progress ไม่เคยลดลงเลยสักติ๊กเดียวตลอดทั้งรอบ / Well Catch = เคยลดได้ แต่ไม่เคยตกต่ำ
      // กว่า 50% หลังจากแตะ 50% ครั้งแรกไปแล้ว (งานวิจัยจาก Fisch จริง - "perfect catch" ถ้า progress ไม่
      // เคยลดเลย ได้โบนัส) ดู runFischFishingSequence ที่ใช้ผลลัพธ์นี้ต่อ
      if (fischReelProgress < progressBeforeTick) everDecreased = true;
      if (fischReelProgress >= 50.0f) reachedHalf = true;
      // เช็คทุกติ๊กหลังจากแตะ 50% ครั้งแรกไปแล้ว (ไม่ใช่แค่ติ๊กที่ >=50 ตอนนี้) ไม่งั้นจังหวะที่ร่วงลงมาต่ำ
      // กว่า 50% จะไม่ถูกจับเลย เพราะเงื่อนไข ">=50" ข้างบนจะเป็นเท็จพอดีตอนนั้น
      if (reachedHalf && fischReelProgress < minProgressSinceHalf) minProgressSinceHalf = fischReelProgress;

      drawFischReelFrame();
      display.display();

      if (fischReelProgress >= 100) {
        playBeepPattern(2, 50, 50);
        fischCaughtPerfect = !everDecreased;
        fischCaughtWell = !fischCaughtPerfect && reachedHalf && minProgressSinceHalf >= 50.0f;
        return true;
      }
      if (fischReelTension >= 100) {
        // Kettle Forged Rod เท่านั้น (ดู FISCH_ROD_IDX_KETTLE_FORGED) - tension เต็ม 100 ครั้งแรกของทริปนี้
        // รีเซ็ตกลับ 0 แทนที่จะขาดเลยทันที (ใช้ได้แค่ครั้งเดียว/ทริป)
        if (fischCache.equippedRodIndex == FISCH_ROD_IDX_KETTLE_FORGED && !fischTensionResetUsed) {
          fischTensionResetUsed = true;
          fischReelTension = 0;
          playBeepPattern(2, 40, 40);
        } else {
          Serial.println("[Fisch] เบ็ดขาด! (tension เต็ม)");
          return false; // เบ็ดขาด - ไม่สนว่า progress ไปถึงไหนแล้ว
        }
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
  const FischFishEntry& caughtEntry = FISCH_CATALOG[fischCurrentFishIndex];

  display.setCursor(0, 0);
  // ต่อท้ายหัวข้อด้วยผล Perfect/Well Catch เลย (ดู runFischReelPhase) ประหยัดที่จอ ไม่ต้องแยกบรรทัด
  if (fischCaughtPerfect) display.print("Caught! PERFECT");
  else if (fischCaughtWell) display.print("Caught! WELL");
  else display.print("Caught!");

  // ป้ายขนาด (Tiny/Small/Big/Giant) ต่อหน้าชื่อ - ตัดให้พอดีจอเสมอกันล้น (ดู fischTruncateToFit)
  String nameLine = fischSizeLabel(fischCaughtWeight, caughtEntry.minWeightKg, caughtEntry.maxWeightKg) +
      " " + caughtEntry.name;
  display.setCursor(0, 16);
  display.print(fischTruncateToFit(nameLine, SCREEN_WIDTH - 4));

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

  // Mutation (ดู FISCH_MUTATIONS/fischRollMutation) - เหตุการณ์หายากมาก โผล่แค่บรรทัดเดียวสั้นๆ
  if (fischCaughtMutationIndex >= 0) {
    display.setCursor(0, 48);
    display.print("* ");
    display.print(FISCH_MUTATIONS[fischCaughtMutationIndex].name);
    display.print(" MUTATION!");
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
  fischResolveEquippedRodStats(); // ครั้งเดียวต่อรอบตกปลา (สำคัญกับ Random Rod - ดูคอมเมนต์ฟังก์ชันนั้น)

  int quality = runFischCastPhase();
  if (fischQuitToMenu) return;

  runFischBiteWaitPhase(quality);
  if (fischQuitToMenu) return;

  bool hooked = runFischHookReactionPhase();
  if (fischQuitToMenu) return;
  if (!hooked) {
    fischCache.totalEscapes++;
    // Ebbtide Rod เท่านั้น (Tenacity - ดู FISCH_ROD_IDX_EBBTIDE) - ปลาหลุด ตั้งค่าไว้ให้ทริปถัดไปได้ rateIn
    // โบนัสครั้งเดียว (ดู runFischReelPhase ที่ใช้/เคลียร์ค่านี้)
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_EBBTIDE) fischTenacityActive = true;
    // Bean Growth Rod เท่านั้น - จับพลาด stack รีเซ็ตกลับ 0
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_BEAN_GROWTH) fischMomentumStackCount = 0;
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
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_EBBTIDE) fischTenacityActive = true;
    if (fischCache.equippedRodIndex == FISCH_ROD_IDX_BEAN_GROWTH) fischMomentumStackCount = 0;
    fischSaveSaveData();
    Serial.println("[Fisch] ปลาหลุด (reel แพ้)");
    fischTransitionTo(drawFischEscapeFrame);
    delay(1400);
    return;
  }

  // น้ำหนักสุ่มไปแล้วตั้งแต่ตอน runFischBiteWaitPhase (ต้องรู้ล่วงหน้าก่อนเข้า Reel เพื่อเช็ค overload) ใช้
  // fischCaughtWeight ตรงนี้ต่อได้เลย ไม่ต้องสุ่มซ้ำ
  const FischFishEntry& f = FISCH_CATALOG[fischCurrentFishIndex];

  fischCaughtMutationIndex = fischRollMutation();

  fischCaughtIsNewSpecies = (fischCache.bestWeightKg[fischCurrentFishIndex] <= 0.0f);
  fischCaughtIsNewBest = fischCaughtIsNewSpecies || (fischCaughtWeight > fischCache.bestWeightKg[fischCurrentFishIndex]);
  if (fischCaughtIsNewBest) fischCache.bestWeightKg[fischCurrentFishIndex] = fischCaughtWeight;
  // ไม่ควรเต็มได้ (เช็คไว้แล้วตอนกด Go Fishing) แต่กันไว้เผื่อจริงๆ - ถ้าใส่ไม่ได้ก็แค่ไม่นับเป็นของที่ถือ
  // อยู่ (ยังนับเป็น catch/best weight ตามปกติ แค่ไม่มีตัวจริงให้ขาย)
  fischAddFishToInventory(fischCurrentFishIndex, fischCaughtWeight, fischCaughtMutationIndex);
  fischCache.totalCatches++;
  fischCache.rodMasteryCatches[fischCache.equippedRodIndex]++; // ดู fischGetMasteryBonus

  // Shoal Twin Rod เท่านั้น (Duplicate Catch - ดู FISCH_ROD_IDX_SHOAL_TWIN) - 15% ได้ปลาตัวเดิมซ้ำอีกตัว
  // (น้ำหนัก/mutation เดียวกันเป๊ะ) ไม่นับเป็น catch/mastery เพิ่ม (แค่ของในกระเป๋าเพิ่ม)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_SHOAL_TWIN && random(0, 100) < 15) {
    if (fischAddFishToInventory(fischCurrentFishIndex, fischCaughtWeight, fischCaughtMutationIndex)) {
      Serial.println("[Fisch] Shoal Twin Rod: ได้ปลาซ้ำอีกตัว!");
    }
  }

  // เกาะที่ต้อง level สูงให้ XP โบนัสเพิ่ม (ดู FISCH_ISLAND_XP_BONUS_DIVISOR) - เกาะเริ่มต้น (levelRequired
  // ต่ำ) แทบไม่มีผลอะไร แต่เกาะปลายทางไกลๆ XP จะพุ่งขึ้นชัดเจน จูงใจให้ไปเกาะไกลจริง ไม่ใช่แค่ตัวเลือกปลา
  float islandXpMult = 1.0f + FISCH_MAPS[fischCache.currentMapIndex].levelRequired / FISCH_ISLAND_XP_BONUS_DIVISOR;
  uint32_t xpGain = (uint32_t)(FISCH_XP_BY_TIER[f.rarity] * islandXpMult + 0.5f);
  // Regal Rod เท่านั้น (ดู FISCH_ROD_IDX_REGAL) - XP ที่ได้ทั้งหมด +25% เพิ่มอีกทาง สะสมกับโบนัสเกาะได้เลย
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_REGAL) xpGain = (uint32_t)(xpGain * 1.25f);
  fischCache.totalXp += xpGain;

  // Hoarder Rod เท่านั้น (ดู FISCH_ROD_IDX_HOARDER) - ได้เหรียญโบนัสเล็กน้อยทุกครั้งที่จับได้ ไม่ว่า rarity ไหน
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_HOARDER) fischCache.coins += 20;

  // Perfect/Well Catch (ดู runFischReelPhase) - ให้เป็นโบนัสทันที ไม่ผูกกับตัวปลาที่ถืออยู่ (ต่างจาก
  // mutation ที่ต้องติดกับตัวปลาไปจนกว่าจะขาย เพราะราคาขายคำนวณทีหลัง) งานวิจัยจาก Fisch จริง - เกมจริงให้
  // โบนัส C$ คงที่ + XP 50% ตอน perfect เราปรับให้ผูกกับ rarity ของตัวที่จับได้แทนตัวเลขคงที่ตายตัว
  // Horizon Seeker Rod เท่านั้น (ดู FISCH_ROD_IDX_HORIZON_SEEKER) - โบนัส Perfect Catch คูณ 2 (100% แทน 50%)
  bool horizonSeeker = (fischCache.equippedRodIndex == FISCH_ROD_IDX_HORIZON_SEEKER);
  if (fischCaughtPerfect) {
    float perfectMult = horizonSeeker ? 1.0f : 0.5f;
    fischCache.totalXp += (uint32_t)(FISCH_XP_BY_TIER[f.rarity] * perfectMult);
    fischCache.coins += (uint32_t)(FISCH_SELL_BASE_BY_TIER[f.rarity] * perfectMult);
  } else if (fischCaughtWell) {
    fischCache.totalXp += FISCH_XP_BY_TIER[f.rarity] / 5;
    fischCache.coins += FISCH_SELL_BASE_BY_TIER[f.rarity] / 5;
  }

  // Bean Growth Rod เท่านั้น (Momentum Stack - ดู FISCH_ROD_IDX_BEAN_GROWTH) - จับสำเร็จแบบ Perfect/Well
  // สะสม stack เพิ่ม (สูงสุด 5) จับธรรมดา (ไม่ perfect/well) stack รีเซ็ตกลับ 0
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_BEAN_GROWTH) {
    if (fischCaughtPerfect || fischCaughtWell) {
      if (fischMomentumStackCount < 5) fischMomentumStackCount++;
    } else {
      fischMomentumStackCount = 0;
    }
  }

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
// Fish Log - "fishdex" เข้ามาแล้วเลือกหมวดหมู่ก่อน (All / General / รายเกาะ) แล้วค่อยไล่ดูทีละตัวด้วย < / >
// ภายในหมวดที่เลือก (กดค้างเลื่อนเร็วได้ - ดู fischHoldRepeatStep) ชนิดที่ยังไม่เคยจับโชว์ "???" (ไม่
// spoiler ชื่อ/rarity) ชนิดที่เคยจับโชว์ชื่อ + rarity + ตัวใหญ่สุดที่เคยได้ (ถาวร ไม่ลดแม้จะขายปลาชนิดนั้น
// จนหมดกระเป๋าแล้วก็ตาม - แยกจาก Inventory ที่โชว์ของที่ถืออยู่ตอนนี้จริงๆ) ชื่อยาวเกินจอตัดด้วย
// fischTruncateToFit กันขึ้นบรรทัดใหม่ไปทับ layout - เพิ่มหมวดหมู่ตอนแคตาล็อกโตขึ้นมาก (120 -> 208 ตัว หลัง
// ใส่ปลาประจำเกาะ) ไล่ดูทีละตัวทั้งหมดแบบเดิมเริ่มไม่สะดวกแล้ว
// ---------------------------------------------------------------------------
int fischLogFilteredIndices[FISCH_CATALOG_COUNT]; // index จริงใน FISCH_CATALOG ที่ผ่าน filter หมวดปัจจุบัน
int fischLogFilteredCount = 0;

int fischLogCategoryCount() { return 2 + FISCH_MAP_COUNT; } // 0=All, 1=General, 2..=รายเกาะ

/// ชื่อหมวดหมู่ตาม index (0=All, 1=General ปลาที่หาได้ทุกเกาะ, 2 ขึ้นไป=ชื่อเกาะจาก FISCH_MAPS)
String fischLogCategoryName(int idx) {
  if (idx == 0) return "All";
  if (idx == 1) return "General";
  return String(FISCH_MAPS[idx - 2].name);
}

/// สร้างรายการ index ที่ผ่าน filter ของหมวดที่เลือกไว้ - เรียกครั้งเดียวตอนกด OK เข้าหมวด ไม่ต้องคำนวณใหม่
/// ทุกเฟรม (208 ตัวไม่เยอะพอจะช้า แต่ไม่มีเหตุผลต้องทำซ้ำระหว่างไล่ดูอยู่แล้ว)
void fischBuildLogFilter(int category) {
  fischLogFilteredCount = 0;
  for (int i = 0; i < FISCH_CATALOG_COUNT; i++) {
    int8_t excl = FISCH_CATALOG[i].exclusiveMapIndex;
    bool include;
    if (category == 0) include = true;
    else if (category == 1) include = (excl == -1);
    else include = (excl == (category - 2));
    if (include) fischLogFilteredIndices[fischLogFilteredCount++] = i;
  }
}

void drawFischLogFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Fish Log ");

  if (fischLogFilteredCount == 0) { // ไม่ควรเกิดจริง (ทุกหมวดมีปลาอย่างน้อย 1 ตัวเสมอ) กันไว้เฉยๆ
    display.print("(empty)");
    fischPrintCentered("No fish here", 28);
    display.setCursor(0, 56);
    display.print("BACK exit");
    return;
  }

  display.print(fischLogIndex + 1);
  display.print("/");
  display.print(fischLogFilteredCount);

  int catIdx = fischLogFilteredIndices[fischLogIndex];
  bool discovered = fischCache.bestWeightKg[catIdx] > 0.0f;
  if (discovered) {
    fischPrintNameCentered(FISCH_CATALOG[catIdx].name, 16); // เลื่อนเองถ้ายาวเกินจอ
  } else {
    display.setCursor(0, 16);
    display.print("???");
  }

  display.setCursor(0, 28);
  display.print("Rarity: ");
  display.print(discovered ? FISCH_RARITY_NAMES[FISCH_CATALOG[catIdx].rarity] : "???");

  if (discovered) {
    display.setCursor(0, 40);
    display.print("Best: ");
    display.print(fischCache.bestWeightKg[catIdx], 2);
    display.print(" kg");
  }

  display.setCursor(0, 56);
  display.print("<> browse BACK exit");
}

/// ไล่ดูปลาภายในหมวดที่เลือกไว้แล้ว (fischBuildLogFilter เรียกไปก่อนหน้านี้แล้ว) BACK กลับไปหน้าเลือกหมวด
void runFischLogBrowseMenu() {
  fischLogIndex = 0;
  fischTransitionTo(drawFischLogFrame);
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  if (fischLogFilteredCount == 0) { // กันหาร 0 ตรง % ด้านล่าง (ไม่ควรเกิดจริงตามคอมเมนต์ข้างบน)
    while (!btnBack.consumeJustPressed()) { delay(30); }
    return;
  }

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischLogIndex = (fischLogIndex + 1) % fischLogFilteredCount;
      drawFischLogFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      fischLogIndex = (fischLogIndex - 1 + fischLogFilteredCount) % fischLogFilteredCount;
      drawFischLogFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    delay(30);
  }
}

void drawFischLogCategoryFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  fischPrintCentered("Fish Log", 4);
  fischPrintNameCentered(String("> ") + fischLogCategoryName(fischLogCategoryIndex), 26);
  fischPrintCentered("<> OK BACK", 56);
}

/// จุดเข้า Fish Log จริง - เลือกหมวดก่อน (All/General/รายเกาะ) แล้วค่อยเข้าไปไล่ดูทีละตัว กด BACK ในหน้าไล่
/// ดูจะกลับมาที่นี่ (ไม่ออกจาก Fish Log ทั้งหมด) เหมือน pattern ของ runFischInventoryMenu
void runFischLogMenu() {
  fischLogCategoryIndex = 0;
  fischTransitionTo(drawFischLogCategoryFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  int catCount = fischLogCategoryCount();
  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischLogCategoryIndex = (fischLogCategoryIndex + 1) % catCount;
      drawFischLogCategoryFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischLogCategoryIndex = (fischLogCategoryIndex - 1 + catCount) % catCount;
      drawFischLogCategoryFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      fischBuildLogFilter(fischLogCategoryIndex);
      runFischLogBrowseMenu();
      fischTransitionTo(drawFischLogCategoryFrame);
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// จัดการกระเป๋าปลา (fischCache.inventory[]) - เก็บปลาทุกตัวที่จับได้เป็นรายการจริง (ไม่ใช่แค่นับจำนวน)
// เพราะราคาขายต้องอิงน้ำหนักของตัวนั้นจริงๆ วนลูปสดทุกครั้ง (แค่ 40 ช่อง เร็วมาก ไม่ต้อง cache)
// ---------------------------------------------------------------------------
int fischCountItemsOfSpecies(int catalogIdx) {
  int count = 0;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex == catalogIdx) count++;
  }
  return count;
}

/// จำนวน "ชนิด" ที่มีอย่างน้อย 1 ตัวอยู่ในกระเป๋าตอนนี้ - ใช้กับเลข n/count บนหน้ารายชื่อปลา (เหมือนเดิม
/// ไม่โชว์ "???" กั้นสปอยล์เหมือน Fish Log เพราะนี่คือของที่ถืออยู่จริง ไม่ใช่ของที่ยังไม่เคยเจอ)
int fischCountOwnedSpecies() {
  int count = 0;
  for (int c = 0; c < FISCH_CATALOG_COUNT; c++) {
    if (fischCountItemsOfSpecies(c) > 0) count++;
  }
  return count;
}

/// หา catalogIndex ของ "ชนิดที่มีของอยู่ ตัวที่ ownedIdx" (เรียงตาม catalogIndex) คืน -1 ถ้าเกินจริง
int fischOwnedCatalogIndex(int ownedIdx) {
  int count = 0;
  for (int c = 0; c < FISCH_CATALOG_COUNT; c++) {
    if (fischCountItemsOfSpecies(c) > 0) {
      if (count == ownedIdx) return c;
      count++;
    }
  }
  return -1;
}

/// หา index ใน fischCache.inventory[] ของ "ตัวที่ itemIdx ของชนิด catalogIdx" คืน -1 ถ้าไม่มีจริง
int fischInventorySlotOfSpeciesItem(int catalogIdx, int itemIdx) {
  int count = 0;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex == catalogIdx) {
      if (count == itemIdx) return i;
      count++;
    }
  }
  return -1;
}

int fischInventoryFreeSlot() {
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex == -1) return i;
  }
  return -1;
}

bool fischInventoryIsFull() {
  return fischInventoryFreeSlot() == -1;
}

bool fischAddFishToInventory(int catalogIdx, float weightKg, int mutationIndex) {
  int slot = fischInventoryFreeSlot();
  if (slot < 0) return false;
  fischCache.inventory[slot].catalogIndex = catalogIdx;
  fischCache.inventory[slot].weightKg = weightKg;
  fischCache.inventory[slot].mutationIndex = mutationIndex;
  fischCache.inventoryCount++;
  return true;
}

void fischRemoveFishFromInventory(int invSlot) {
  fischCache.inventory[invSlot].catalogIndex = -1;
  fischCache.inventory[invSlot].weightKg = 0;
  fischCache.inventory[invSlot].mutationIndex = -1;
  if (fischCache.inventoryCount > 0) fischCache.inventoryCount--;
}

/// ราคาขาย = ราคาฐานตาม rarity + (น้ำหนัก x ราคาต่อกิโลตาม rarity) แล้วคูณด้วย mutation multiplier ถ้ามี
/// (mutationIndex -1 = ไม่กลายพันธุ์ = คูณ 1 เฉยๆ) - ปลาหายากมีมูลค่าพื้นฐานสูงกว่าเสมอ แต่ตัวใหญ่ในชนิด
/// เดียวกันก็ขายได้แพงกว่าตัวเล็กด้วย
uint32_t fischSellPriceFor(FischRarity rarity, float weightKg, int mutationIndex) {
  float price = FISCH_SELL_BASE_BY_TIER[rarity] + weightKg * FISCH_SELL_PER_KG_BY_TIER[rarity];
  if (mutationIndex >= 0) price *= FISCH_MUTATIONS[mutationIndex].multiplier;
  if (price < 1) price = 1;
  return (uint32_t)(price + 0.5f);
}

// ---------------------------------------------------------------------------
// เลเวลผู้เล่น - ไม่เก็บ level ใน save data ตรงๆ คำนวณจาก totalXp เสมอ (กันไม่ให้สอง field ไม่ตรงกัน) ไม่มี
// เพดานตายตัว เก็บ XP ไปเรื่อยๆ สูตรเพิ่มขึ้นแบบสามเหลี่ยม (ยิ่งเลเวลสูงยิ่งต้องสะสมเยอะขึ้นเรื่อยๆ ไม่ใช่
// เส้นตรง) ใช้ปลดล็อกเกาะใหม่/เบ็ดบางรุ่นที่ต้องถึงเลเวล (ยังไม่ทำเกาะจริงตอนนี้ - โครงเลเวลพร้อมไว้ก่อน)
// ---------------------------------------------------------------------------
uint32_t fischXpForLevel(int level) {
  // level 1 ต้องเป็น 0 เสมอ (ทุกคนเริ่มที่เลเวล 1 ฟรีอยู่แล้วไม่ต้องมี XP เลย) เดิมสูตรนี้ใช้ level ตรงๆ
  // ทำให้ fischXpForLevel(1) กลายเป็น 50 ไม่ใช่ 0 - พอ Stats หน้าคำนวณ totalXp(0) - xpAtLevel(50) บน
  // uint32_t เลยพันกลับ (underflow) กลายเป็นเลขมหาศาลแบบ 4294967286 แทนที่จะเป็นค่าติดลบตามที่ควรจะเป็น
  if (level <= 1) return 0;
  int n = level - 1;
  // ตัวคูณ 40 (เคยลอง 50 -> 60 มาแล้ว แต่เล่นจริง 2 ชม.ได้แค่ lvl 16 - ช้าไปเมื่อรวมกับโอกาสของหายากที่ผ่อน
  // ไปพร้อมกัน (ดู fischRollRarity) 40 คือจุดกลางระหว่าง 50 เดิม (ง่ายไป) กับ 60 (ช้าไปตามที่เล่นจริง)
  return (uint32_t)40 * n * (n + 1) / 2;
}

int fischLevelForXp(uint32_t xp) {
  int level = 1;
  while (fischXpForLevel(level + 1) <= xp) level++;
  return level;
}

int fischCurrentLevel() {
  return fischLevelForXp(fischCache.totalXp);
}

/// index 0 (Basic Rod) ถือว่ามีเสมอ ไม่ต้องเช็ค rodOwned[] - fischDebugUnlockAllRods (Debug Mode) ลัดผ่านได้
/// โดยไม่แตะ rodOwned[] จริงเลย ปิด toggle เมื่อไหร่กลับสู่สถานะจริงทันที
bool fischOwnsRod(int rodIdx) {
  if (rodIdx == 0 || fischDebugUnlockAllRods) return true;
  return fischCache.rodOwned[rodIdx];
}

bool fischOwnsMap(int mapIdx) {
  if (mapIdx == 0 || fischDebugUnlockAllIslands) return true;
  return fischCache.mapUnlocked[mapIdx];
}

/// คำนวณค่าพลังจริงของเบ็ดที่ใส่อยู่ เรียกครั้งเดียวตอนเริ่ม Go Fishing แต่ละรอบ (ไม่ใช่ทุกเฟสย่อย) เพราะ
/// Random Rod ต้องสุ่มค่าใหม่แค่ครั้งเดียวต่อรอบตกปลา ไม่งั้น Cast ได้ค่านึง Reel ได้อีกค่านึงไม่สมเหตุสมผล
/// - เก็บผลไว้ใน fischActive* ให้ Cast/Hook/Reel อ่านตรงๆ ไม่ต้องยุ่งกับ FISCH_RODS/isRandom เอง
void fischResolveEquippedRodStats() {
  const FischRodEntry& rod = FISCH_RODS[fischCache.equippedRodIndex];
  if (rod.isRandom) {
    fischActiveResilienceBonus = random(FISCH_RANDOM_ROD_MIN_STAT, FISCH_RANDOM_ROD_MAX_STAT + 1);
    fischActiveLuckBonus = random(FISCH_RANDOM_ROD_MIN_STAT, FISCH_RANDOM_ROD_MAX_STAT + 1);
    fischActiveHookMsBonus = random(FISCH_RANDOM_ROD_MIN_HOOK_MS, FISCH_RANDOM_ROD_MAX_HOOK_MS + 1);
    fischActiveHookMissBonus = random(FISCH_RANDOM_ROD_MIN_MISS, FISCH_RANDOM_ROD_MAX_MISS + 1);
    fischActiveControlBonus = random(FISCH_RANDOM_ROD_MIN_CONTROL, FISCH_RANDOM_ROD_MAX_CONTROL + 1);
    fischActiveProgressRateBonus = random(FISCH_RANDOM_ROD_MIN_PROGRESS_RATE, FISCH_RANDOM_ROD_MAX_PROGRESS_RATE + 1);
    fischActiveLureSpeedBonus = random(FISCH_RANDOM_ROD_MIN_STAT, FISCH_RANDOM_ROD_MAX_STAT + 1);
    fischActiveMaxWeightKg = FISCH_RANDOM_ROD_MIN_WEIGHT +
        (random(0, 10001) / 10000.0f) * (FISCH_RANDOM_ROD_MAX_WEIGHT - FISCH_RANDOM_ROD_MIN_WEIGHT);
    fischActiveWormholeChancePercent = 0; // Random Rod ไม่มี Wormhole (เป็นความสามารถเฉพาะเบ็ดจริงเท่านั้น)
  } else {
    fischActiveResilienceBonus = rod.resilienceBonus;
    fischActiveLuckBonus = rod.luckBonus;
    fischActiveHookMsBonus = rod.hookMsBonus;
    fischActiveControlBonus = rod.controlBonus;
    fischActiveHookMissBonus = rod.hookMissBonus;
    fischActiveProgressRateBonus = rod.progressRateBonus;
    fischActiveLureSpeedBonus = rod.lureSpeedBonus;
    fischActiveMaxWeightKg = rod.maxWeightKg;
    fischActiveWormholeChancePercent = rod.wormholeChancePercent;
  }

  // Rod Mastery (ดู fischGetMasteryBonus) - บวกเพิ่มทับสเตตที่ได้จากตารางเบ็ด/สุ่มด้านบนอีกที ใช้ได้กับ
  // Random Rod ด้วย (นับ catch สะสมของ "ช่อง" Random Rod เอง ไม่ผูกกับค่าที่สุ่มได้เฉพาะรอบนั้นๆ)
  int masteryLuck;
  float masteryWeight;
  fischGetMasteryBonus(fischCache.rodMasteryCatches[fischCache.equippedRodIndex], masteryLuck, masteryWeight);
  fischActiveLuckBonus += masteryLuck;
  fischActiveMaxWeightKg += masteryWeight;

  // Bait (ดู FISCH_BAITS) - ไม่ใส่เหยื่อเลยโดนบทลงโทษเล็กน้อย เหยื่อพื้นฐานสุดแค่ดึงกลับมาเท่าปกติพอดี
  if (fischCache.equippedBaitIndex < 0) {
    fischActiveLuckBonus += FISCH_NO_BAIT_LUCK_PENALTY;
    fischActiveLureSpeedBonus += FISCH_NO_BAIT_LURE_PENALTY;
  } else {
    const FischBaitEntry& bait = FISCH_BAITS[fischCache.equippedBaitIndex];
    fischActiveLuckBonus += bait.luckBonus;
    fischActiveLureSpeedBonus += bait.lureSpeedBonus;
  }

  // Bean Growth Rod เท่านั้น (Momentum Stack - ดู FISCH_ROD_IDX_BEAN_GROWTH) - Perfect/Well Catch ติดกัน
  // สะสม stack ไว้ (อัปเดตท้าย runFischFishingSequence) ทริปถัดไปได้ luck เพิ่มตาม stack ที่มี (+5/stack)
  if (fischCache.equippedRodIndex == FISCH_ROD_IDX_BEAN_GROWTH) {
    fischActiveLuckBonus += fischMomentumStackCount * 5;
  }
}

// ---------------------------------------------------------------------------
// Inventory - กด OK เข้าเมนูย่อยก่อนเสมอ: Fish / Rod (ดูอย่างเดียวทั้งคู่ - ขาย/ซื้อจริงอยู่ใน Shop) หน้า
// รายชื่อปลา (drawFischFishListFrame) กับหน้าไล่ดูราย ตัว (drawFischItemBrowseFrame) และหน้าไล่ดูเบ็ด
// (drawFischRodBrowseFrame) เป็นฟังก์ชันร่วมกับ Shop ทั้งหมด (ต่างกันแค่ sellMode/shopMode) ดูจุดที่ Shop
// เรียกใช้ด้านล่างไฟล์
// ---------------------------------------------------------------------------
int fischBrowseCatalogIdx = -1; // ชนิดปลาที่กำลังไล่ดูราย ตัวอยู่ (ตั้งก่อนเรียก runFischItemBrowseMenu)

const char* const FISCH_INV_CATEGORY_NAMES[] = { "> Fish", "> Rod", "> Bait" };
const int FISCH_INV_CATEGORY_COUNT = 3;

void drawFischInventoryTopFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  fischPrintCentered("Inventory", 4);
  fischPrintCentered(FISCH_INV_CATEGORY_NAMES[fischInvCategoryIndex], 26);
  fischPrintCentered("<> OK BACK", 56);
}

void runFischInventoryMenu() {
  fischInvCategoryIndex = 0;
  fischTransitionTo(drawFischInventoryTopFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischInvCategoryIndex = (fischInvCategoryIndex + 1) % FISCH_INV_CATEGORY_COUNT;
      drawFischInventoryTopFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischInvCategoryIndex = (fischInvCategoryIndex - 1 + FISCH_INV_CATEGORY_COUNT) % FISCH_INV_CATEGORY_COUNT;
      drawFischInventoryTopFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      if (fischInvCategoryIndex == 0) {
        fischFishBrowseSellMode = false;
        runFischFishListMenu(false);
      } else if (fischInvCategoryIndex == 1) {
        fischRodBrowseShopMode = false;
        runFischRodBrowseMenu(false);
      } else {
        runFischBaitBrowseMenu(false, -1); // Inventory > Bait โชว์แค่เหยื่อทั่วไป (เฉพาะเกาะดูที่ตัวเกาะเอง)
      }
      fischTransitionTo(drawFischInventoryTopFrame);
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

/// รายชื่อปลาที่มีอยู่ตอนนี้ (จัดกลุ่มตามชนิด) - ใช้ร่วมกัน Inventory>Fish (sellMode=false) กับ Shop>Sell
/// Fish (sellMode=true) หัวเรื่องเปลี่ยนตาม mode เฉยๆ พฤติกรรมเหมือนกันทุกอย่าง (OK เข้าไปดูราย ตัว)
/// รวมมูลค่าขายของทั้งกระเป๋า (ทุกชนิดรวมกัน) - ใช้ทั้งโชว์ตัวเลขก่อนขายและตอนขายจริง (ดู "Sell All")
uint32_t fischTotalInventoryValue() {
  uint32_t total = 0;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex < 0) continue;
    FischRarity r = FISCH_CATALOG[fischCache.inventory[i].catalogIndex].rarity;
    total += fischSellPriceFor(r, fischCache.inventory[i].weightKg, fischCache.inventory[i].mutationIndex);
  }
  return total;
}

/// ขายทุกตัวในกระเป๋าทีเดียว (ทุกชนิด) คืนยอดที่ขายได้
uint32_t fischSellAllInventory() {
  uint32_t total = 0;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex < 0) continue;
    FischRarity r = FISCH_CATALOG[fischCache.inventory[i].catalogIndex].rarity;
    total += fischSellPriceFor(r, fischCache.inventory[i].weightKg, fischCache.inventory[i].mutationIndex);
    fischRemoveFishFromInventory(i);
  }
  return total;
}

void drawFischFishListFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  int ownedCount = fischCountOwnedSpecies();
  if (ownedCount == 0) {
    fischPrintCentered("No fish to show", 24);
    fischPrintCentered("Go catch something!", 36);
    fischPrintCentered("BACK exit", 56);
    return;
  }

  // -1 = ตัวเลือกพิเศษ "Sell All" (ขายทั้งกระเป๋าทีเดียว) มีแค่ตอน sellMode เท่านั้น (ดู Bait "No Bait" -
  // pattern เดียวกัน) เรียงเป็นตัวแรกเสมอ
  int totalCount = ownedCount + (fischFishBrowseSellMode ? 1 : 0);
  display.setCursor(0, 0);
  display.print(fischFishBrowseSellMode ? "Sell Fish " : "Fish ");
  display.print(fischInvIndex + 1 + (fischFishBrowseSellMode ? 1 : 0));
  display.print("/");
  display.print(totalCount);

  if (fischFishBrowseSellMode && fischInvIndex == -1) {
    fischPrintNameCentered("Sell All", 16);
    display.setCursor(0, 28);
    display.print("Species: ");
    display.print(ownedCount);
    display.setCursor(0, 40);
    display.print("Total: ");
    display.print(fischTotalInventoryValue());
    display.setCursor(0, 56);
    display.print("<> browse OK sell");
    return;
  }

  int catIdx = fischOwnedCatalogIndex(fischInvIndex);

  fischPrintNameCentered(FISCH_CATALOG[catIdx].name, 16);
  display.setCursor(0, 28);
  display.print("Rarity: ");
  display.print(FISCH_RARITY_NAMES[FISCH_CATALOG[catIdx].rarity]);
  display.setCursor(0, 40);
  display.print("Have: ");
  display.print(fischCountItemsOfSpecies(catIdx));

  display.setCursor(0, 56);
  display.print("<> browse OK view");
}

void runFischFishListMenu(bool sellMode) {
  fischFishBrowseSellMode = sellMode;
  fischInvIndex = sellMode ? -1 : 0; // -1 = "Sell All" ตัวเลือกพิเศษ มีแค่ตอน sellMode
  fischTransitionTo(drawFischFishListFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    int ownedCount = fischCountOwnedSpecies();
    int minIdx = sellMode ? -1 : 0;
    bool stepNext = fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat);
    bool stepPrev = fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat);
    if (ownedCount > 0) {
      if (stepNext) {
        fischInvIndex++;
        if (fischInvIndex >= ownedCount) fischInvIndex = minIdx;
        drawFischFishListFrame();
        display.display();
      }
      if (stepPrev) {
        fischInvIndex--;
        if (fischInvIndex < minIdx) fischInvIndex = ownedCount - 1;
        drawFischFishListFrame();
        display.display();
      }
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed() && ownedCount > 0) {
      if (sellMode && fischInvIndex == -1) {
        // มี confirm ก่อนขายจริงเสมอ กันกดพลาด (เหมือนขายทีละตัว) โชว์ยอดรวมให้เห็นก่อนตัดสินใจ
        if (fischConfirmOverlay("Sell everything?")) {
          uint32_t total = fischSellAllInventory();
          fischCache.coins += total;
          fischSaveSaveData();
          Serial.print("[Fisch] ขายทั้งกระเป๋าได้ ");
          Serial.println(total);
          playBeepPattern(3, 60, 60);
          fischInvIndex = -1;
        }
        fischTransitionTo(drawFischFishListFrame);
        while (btnOk.isPressed()) { delay(20); }
        btnOk.lastPressed = false;
        continue;
      }
      fischBrowseCatalogIdx = fischOwnedCatalogIndex(fischInvIndex);
      runFischItemBrowseMenu(fischBrowseCatalogIdx, sellMode);
      // ขายจนหมดชนิดนี้ไปเลยได้ - จำนวนชนิดอาจลดลง กัน index ค้างเกินขอบเขต
      int newOwnedCount = fischCountOwnedSpecies();
      if (newOwnedCount == 0) { fischInvIndex = minIdx; }
      else if (fischInvIndex >= newOwnedCount) { fischInvIndex = newOwnedCount - 1; }
      fischTransitionTo(drawFischFishListFrame);
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

/// ไล่ดูปลาแต่ละตัวจริงๆของชนิดเดียว (แต่ละตัวน้ำหนักไม่เท่ากัน) - sellMode=true มีปุ่มขาย (Shop) sellMode=
/// false ดูอย่างเดียว (Inventory) ใช้ fischBrowseCatalogIdx (ตั้งไว้จากผู้เรียก) + fischInvItemIndex
/// รวมมูลค่าขายของ "ชนิดเดียว" ทุกตัวที่ถืออยู่ (ต่างจาก fischTotalInventoryValue ที่รวมทุกชนิด)
uint32_t fischSpeciesTotalValue(int catalogIdx) {
  uint32_t total = 0;
  FischRarity r = FISCH_CATALOG[catalogIdx].rarity;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex != catalogIdx) continue;
    total += fischSellPriceFor(r, fischCache.inventory[i].weightKg, fischCache.inventory[i].mutationIndex);
  }
  return total;
}

/// ขายทุกตัวของชนิดเดียวทีเดียว คืนยอดที่ขายได้
uint32_t fischSellAllOfSpecies(int catalogIdx) {
  uint32_t total = 0;
  FischRarity r = FISCH_CATALOG[catalogIdx].rarity;
  for (int i = 0; i < FISCH_INVENTORY_CAPACITY; i++) {
    if (fischCache.inventory[i].catalogIndex != catalogIdx) continue;
    total += fischSellPriceFor(r, fischCache.inventory[i].weightKg, fischCache.inventory[i].mutationIndex);
    fischRemoveFishFromInventory(i);
  }
  return total;
}

void drawFischItemBrowseFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  int itemCount = fischCountItemsOfSpecies(fischBrowseCatalogIdx);
  if (itemCount == 0) {
    fischPrintCentered("All sold!", 28);
    fischPrintCentered("BACK exit", 56);
    return;
  }

  // -1 = ตัวเลือกพิเศษ "Sell All" (ขายทั้งชนิดนี้ทีเดียว) มีแค่ตอน sellMode เท่านั้น (เหมือน pattern เดียวกับ
  // Fish List ด้านบน/Bait "No Bait")
  int totalCount = itemCount + (fischFishBrowseSellMode ? 1 : 0);
  display.setCursor(0, 0);
  display.print("Item ");
  display.print(fischInvItemIndex + 1 + (fischFishBrowseSellMode ? 1 : 0));
  display.print("/");
  display.print(totalCount);

  if (fischFishBrowseSellMode && fischInvItemIndex == -1) {
    fischPrintNameCentered(String("Sell All ") + FISCH_CATALOG[fischBrowseCatalogIdx].name, 14);
    display.setCursor(0, 28);
    display.print("Have: ");
    display.print(itemCount);
    display.setCursor(0, 40);
    display.print("Total: ");
    display.print(fischSpeciesTotalValue(fischBrowseCatalogIdx));
    display.setCursor(0, 56);
    display.print("<> browse OK sell");
    return;
  }

  int slot = fischInventorySlotOfSpeciesItem(fischBrowseCatalogIdx, fischInvItemIndex);
  float weight = fischCache.inventory[slot].weightKg;
  int mutationIdx = fischCache.inventory[slot].mutationIndex;
  FischRarity rarity = FISCH_CATALOG[fischBrowseCatalogIdx].rarity;

  fischPrintNameCentered(FISCH_CATALOG[fischBrowseCatalogIdx].name, 14);

  display.setCursor(0, 28);
  display.print("Weight: ");
  display.print(weight, 2);
  display.print(" kg");
  // Mutation (ดู FISCH_MUTATIONS) ติดกับตัวปลาตัวนี้โดยเฉพาะ ไม่ใช่ทุกตัวของชนิดเดียวกัน
  if (mutationIdx >= 0) {
    display.print(" *");
    display.print(FISCH_MUTATIONS[mutationIdx].name);
  }

  if (fischFishBrowseSellMode) {
    display.setCursor(0, 40);
    display.print("Sell for: ");
    display.print(fischSellPriceFor(rarity, weight, mutationIdx));
    display.setCursor(0, 56);
    display.print("<> browse OK sell");
  } else {
    display.setCursor(0, 56);
    display.print("<> browse BACK exit");
  }
}

void runFischItemBrowseMenu(int catalogIdx, bool sellMode) {
  fischBrowseCatalogIdx = catalogIdx;
  fischInvItemIndex = sellMode ? -1 : 0; // -1 = "Sell All" ตัวเลือกพิเศษ มีแค่ตอน sellMode
  fischTransitionTo(drawFischItemBrowseFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    int itemCount = fischCountItemsOfSpecies(fischBrowseCatalogIdx);
    if (itemCount == 0) {
      if (btnBack.consumeJustPressed()) return;
      delay(30);
      continue;
    }
    int minIdx = sellMode ? -1 : 0;
    bool stepNext = fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat);
    bool stepPrev = fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat);
    if (stepNext) {
      fischInvItemIndex++;
      if (fischInvItemIndex >= itemCount) fischInvItemIndex = minIdx;
      drawFischItemBrowseFrame();
      display.display();
    }
    if (stepPrev) {
      fischInvItemIndex--;
      if (fischInvItemIndex < minIdx) fischInvItemIndex = itemCount - 1;
      drawFischItemBrowseFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (sellMode && btnOk.consumeJustPressed()) {
      if (fischInvItemIndex == -1) {
        // มี confirm ก่อนขายจริงเสมอ กันกดพลาด โชว์ยอดรวมของชนิดนี้ให้เห็นก่อนตัดสินใจ
        if (fischConfirmOverlay("Sell all of this?")) {
          uint32_t total = fischSellAllOfSpecies(fischBrowseCatalogIdx);
          fischCache.coins += total;
          fischSaveSaveData();
          Serial.print("[Fisch] ขายทั้งชนิดได้ ");
          Serial.println(total);
          playBeepPattern(3, 60, 60);
          drawFischItemBrowseFrame();
          display.display();
          delay(700);
          return; // ชนิดนี้หมดกระเป๋าแล้วแน่ๆ กลับไปหน้ารายชื่อเลย
        }
        drawFischItemBrowseFrame();
        display.display();
        while (btnOk.isPressed()) { delay(20); }
        btnOk.lastPressed = false;
        continue;
      }
      // มี confirm ก่อนขายจริงเสมอ กันกดพลาด (เจอจากเล่นจริงว่าขายทันทีไม่มีเบรกเลยเสี่ยงขายผิดตัว)
      if (fischConfirmOverlay("Sell this fish?")) {
        int slot = fischInventorySlotOfSpeciesItem(fischBrowseCatalogIdx, fischInvItemIndex);
        float weight = fischCache.inventory[slot].weightKg;
        int mutationIdx = fischCache.inventory[slot].mutationIndex;
        FischRarity rarity = FISCH_CATALOG[fischBrowseCatalogIdx].rarity;
        uint32_t price = fischSellPriceFor(rarity, weight, mutationIdx);

        fischRemoveFishFromInventory(slot);
        fischCache.coins += price;
        fischSaveSaveData();
        Serial.print("[Fisch] ขายปลาได้ ");
        Serial.print(price);
        Serial.println(" เหรียญ");
        playBeep(50);

        int newItemCount = fischCountItemsOfSpecies(fischBrowseCatalogIdx);
        if (newItemCount == 0) { drawFischItemBrowseFrame(); display.display(); delay(700); return; }
        if (fischInvItemIndex >= newItemCount) fischInvItemIndex = newItemCount - 1;
      }
      drawFischItemBrowseFrame();
      display.display();
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

/// ไล่ดูเบ็ดทั้ง 18 อัน - shopMode=false (Inventory>Rod): เบ็ดที่ยังไม่มีโชว์ "???" เลือกได้แค่ใส่เบ็ดที่
/// มีอยู่แล้ว / shopMode=true (Shop>Buy Rods): โชว์ทุกอันเต็มๆเสมอ (ต้องเห็นก่อนซื้อ) ซื้อเบ็ดที่ยังไม่มีได้
/// ด้วย ซื้อเสร็จใส่ให้อัตโนมัติเลย
void drawFischRodBrowseFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  const FischRodEntry& rod = FISCH_RODS[fischRodBrowseIndex];
  bool owned = fischOwnsRod(fischRodBrowseIndex);
  bool reveal = owned || fischRodBrowseShopMode; // Shop โชว์ทุกอันเต็มๆเสมอ Inventory ซ่อนของที่ยังไม่มี

  display.setCursor(0, 0);
  display.print("Rod ");
  display.print(fischRodBrowseIndex + 1);
  display.print("/");
  display.print(FISCH_ROD_COUNT);
  if (fischRodBrowseIndex == fischCache.equippedRodIndex) display.print(" *equipped");

  if (!reveal) {
    fischPrintCentered("???", 26);
    display.setCursor(0, 56);
    display.print("<> browse BACK exit");
    return;
  }

  fischPrintNameCentered(rod.name, 13);

  // ย่อชื่อสถิติเหลือตัวเดียว (R=Resilience, L=Luck, S=Lure Speed, P=progressRateBonus) เพราะพื้นที่จอเหลือ
  // น้อย - ต้องยัดหลายค่าในบรรทัดเดียวให้พอ (ดูคำอธิบายเต็มได้ตอนซื้อ/หน้า Shop ถ้าจำไม่ได้ว่าตัวไหนคืออะไร)
  // Control (controlBonus) ตั้งใจไม่โชว์ตรงนี้เลย ไม่ว่าในเมนู Rod ของ Inventory หรือ Shop - เป็นสถิติลับที่
  // อยากให้ผู้เล่นรู้สึกเอาจากการเล่นจริง ไม่ใช่ตัวเลขที่อ่านได้ตรงๆ จากสเปค (ต่างจากค่าอื่นที่เหลือซึ่ง
  // โปร่งใสให้ดูได้เต็มที่) โชว์เฉพาะใน Serial debug log ตอนใส่เบ็ด (ดู runFischRodBrowseMenu) เผื่อเวลาแก้
  // บั๊ก/จูนค่า ไม่ใช่สิ่งที่ผู้เล่นเห็นบนจอ
  display.setCursor(0, 23);
  if (rod.isRandom) {
    display.print("R:? L:? S:?");
  } else {
    display.print("R");
    display.print(rod.resilienceBonus >= 0 ? "+" : "");
    display.print(rod.resilienceBonus);
    display.print(" L");
    display.print(rod.luckBonus >= 0 ? "+" : "");
    display.print(rod.luckBonus);
    // S (lureSpeedBonus) กับ P (progressRateBonus) แทบทุกเบ็ดเป็น 0 - โชว์เฉพาะตอนไม่ใช่ 0 กันรก
    if (rod.lureSpeedBonus != 0) {
      display.print(" S");
      display.print(rod.lureSpeedBonus >= 0 ? "+" : "");
      display.print(rod.lureSpeedBonus);
    }
    if (rod.progressRateBonus != 0) {
      display.print(" P");
      display.print(rod.progressRateBonus >= 0 ? "+" : "");
      display.print(rod.progressRateBonus);
    }
    // W (Wormhole) มีผลจริงแค่ Wanderer's Rod กับเบ็ด endgame 6 ตัว - โชว์เฉพาะตอนไม่ใช่ 0 เหมือน S/P
    if (rod.wormholeChancePercent != 0) {
      display.print(" W");
      display.print(rod.wormholeChancePercent);
      display.print("%");
    }
  }

  display.setCursor(0, 33);
  if (rod.isRandom) {
    display.print("Hook:? Miss:?");
  } else {
    display.print("Hook");
    display.print(rod.hookMsBonus >= 0 ? "+" : "");
    display.print(rod.hookMsBonus);
    display.print("ms Miss");
    display.print(rod.hookMissBonus >= 0 ? "+" : "");
    display.print(rod.hookMissBonus);
  }

  // แถวนี้ยัด level ต้องใช้ + ราคา/สถานะ + น้ำหนักสูงสุดไว้บรรทัดเดียว - พอเบ็ด endgame ราคาขึ้นหลักล้านแล้ว
  // ("Cost: 2500000") ต้องย่อด้วย fischFormatCoins ไม่งั้นล้นจอ 128px แน่ๆที่ text size 1 (~21 ตัวอักษรพอดี)
  // ไม่โชว์ "Lv" ตอนเป็นเจ้าของแล้ว (ไม่มีความหมายอีกต่อไป เอาที่ว่างไปให้ Max แทน)
  display.setCursor(0, 45);
  if (owned) {
    display.print("Owned");
  } else {
    display.print("Lv");
    display.print(rod.levelRequired);
    display.print(" $");
    display.print(fischFormatCoins(rod.cost));
  }
  // น้ำหนักปลาสูงสุดที่รับไหว (ดู FischRodEntry.maxWeightKg) - Random Rod สุ่มค่านี้ใหม่ทุกรอบเหมือนสเตต
  // อื่นๆของมัน เลยบอกไม่ได้ล่วงหน้า โชว์ "?" แทนแบบเดียวกับ R/L/P ด้านบน
  display.print(" W:");
  if (rod.isRandom) {
    display.print("?");
  } else {
    display.print((int)rod.maxWeightKg);
    display.print("kg");
  }

  display.setCursor(0, 56);
  if (owned) {
    display.print("<> browse OK equip");
  } else if (fischRodBrowseShopMode) {
    display.print("<> browse OK buy");
  } else {
    display.print("<> browse BACK exit");
  }
}

void runFischRodBrowseMenu(bool shopMode) {
  fischRodBrowseShopMode = shopMode;
  fischRodBrowseIndex = 0;
  fischTransitionTo(drawFischRodBrowseFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischRodBrowseIndex = (fischRodBrowseIndex + 1) % FISCH_ROD_COUNT;
      drawFischRodBrowseFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      fischRodBrowseIndex = (fischRodBrowseIndex - 1 + FISCH_ROD_COUNT) % FISCH_ROD_COUNT;
      drawFischRodBrowseFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      bool owned = fischOwnsRod(fischRodBrowseIndex);
      if (owned) {
        fischCache.equippedRodIndex = (uint8_t)fischRodBrowseIndex;
        fischSaveSaveData();
        Serial.print("[Fisch] ใส่เบ็ด: ");
        Serial.print(FISCH_RODS[fischRodBrowseIndex].name);
        Serial.print(" (controlBonus ลับ = ");
        Serial.print(FISCH_RODS[fischRodBrowseIndex].controlBonus);
        Serial.println(", ไม่โชว์บนจอตั้งใจ)");
        playBeep(50);
        drawFischRodBrowseFrame();
        display.display();
      } else if (fischRodBrowseShopMode) {
        const FischRodEntry& shopRod = FISCH_RODS[fischRodBrowseIndex];
        uint32_t cost = shopRod.cost;
        if (fischCurrentLevel() < shopRod.levelRequired) {
          if (displayReady) {
            display.clearDisplay();
            display.setTextSize(1);
            fischPrintCentered(String("Need Lv.") + shopRod.levelRequired, 28);
            display.display();
          }
          delay(900);
          drawFischRodBrowseFrame();
          display.display();
        } else if (fischCache.coins < cost) {
          if (displayReady) {
            display.clearDisplay();
            display.setTextSize(1);
            fischPrintCentered("Not enough coins", 28);
            display.display();
          }
          delay(900);
          drawFischRodBrowseFrame();
          display.display();
        } else if (fischConfirmOverlay("Buy this rod?")) {
          fischCache.coins -= cost;
          fischCache.rodOwned[fischRodBrowseIndex] = true;
          fischCache.equippedRodIndex = (uint8_t)fischRodBrowseIndex;
          fischSaveSaveData();
          Serial.print("[Fisch] ซื้อเบ็ด: ");
          Serial.println(FISCH_RODS[fischRodBrowseIndex].name);
          playBeepPattern(2, 60, 60);
          drawFischRodBrowseFrame();
          display.display();
        } else {
          drawFischRodBrowseFrame();
          display.display();
        }
      }
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Bait - โครงเดียวกับ Rod browse (drawFischRodBrowseFrame/runFischRodBrowseMenu) แต่มีตัวเลือกพิเศษ "No
// Bait" (index -1) แทรกอยู่เสมอที่จุดเริ่มไล่ดู ไม่ต้องซื้อ/เป็นเจ้าของ เลือกได้ตลอด (เท่ากับถอดเหยื่อออก -
// โดนบทลงโทษ FISCH_NO_BAIT_*_PENALTY แทน ดู fischResolveEquippedRodStats) ไม่มี level gate เหมือนเบ็ด
//
// filter ตาม exclusiveMapIndex ก่อนไล่ดูเสมอ (เหมือน Fish Log แยกหมวดหมู่) mapFilter=-1 = โชว์แค่เหยื่อ
// ทั่วไป (Shop กลาง/Inventory - ตรงกับค่า exclusiveMapIndex ของเหยื่อทั่วไปพอดี), mapFilter>=0 = โชว์แค่
// เหยื่อเฉพาะเกาะนั้น (Shop ที่เกาะ) - fischBaitBrowseIndex เป็น index เข้า fischBaitFilteredIndices[]
// ไม่ใช่ index ตรงเข้า FISCH_BAITS อีกต่อไป
// ---------------------------------------------------------------------------
int fischBaitFilteredIndices[FISCH_BAIT_COUNT];
int fischBaitFilteredCount = 0;

/// index 0 (Basic Bait) ถือว่ามีเสมอ เหมือน Basic Rod - แก้ปัญหาที่ผู้เล่นใหม่โดนบทลงโทษ "ไม่มีเหยื่อ"
/// ตั้งแต่ตัวแรกทั้งที่ยังไม่รู้จักระบบนี้เลยด้วยซ้ำ (ดู fischResolveEquippedRodStats/fischResetSaveDataToEmpty)
bool fischOwnsBait(int baitIdx) {
  if (baitIdx == 0) return true;
  return fischCache.baitOwned[baitIdx];
}

void fischBuildBaitFilter(int mapFilter) {
  fischBaitFilteredCount = 0;
  for (int i = 0; i < FISCH_BAIT_COUNT; i++) {
    if (FISCH_BAITS[i].exclusiveMapIndex == mapFilter) {
      fischBaitFilteredIndices[fischBaitFilteredCount++] = i;
    }
  }
}

void drawFischBaitBrowseFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  display.setCursor(0, 0);
  display.print("Bait ");
  display.print(fischBaitBrowseIndex + 2); // -1 -> 1 (No Bait นับเป็นตัวแรกเสมอ)
  display.print("/");
  display.print(fischBaitFilteredCount + 1);

  int realIdx = (fischBaitBrowseIndex >= 0) ? fischBaitFilteredIndices[fischBaitBrowseIndex] : -1;
  if (realIdx == fischCache.equippedBaitIndex) display.print(" *equipped");

  if (fischBaitBrowseIndex == -1) {
    fischPrintNameCentered("No Bait", 13);
    display.setCursor(0, 28);
    display.print("L");
    display.print(FISCH_NO_BAIT_LUCK_PENALTY);
    display.print(" S");
    display.print(FISCH_NO_BAIT_LURE_PENALTY);
    display.setCursor(0, 45);
    display.print("Always available");
    display.setCursor(0, 56);
    display.print("<> browse OK equip");
    return;
  }

  const FischBaitEntry& bait = FISCH_BAITS[realIdx];
  bool owned = fischOwnsBait(realIdx);
  bool reveal = owned || fischBaitBrowseShopMode; // Shop โชว์ทุกอันเต็มๆเสมอ Inventory ซ่อนของที่ยังไม่มี

  if (!reveal) {
    fischPrintCentered("???", 26);
    display.setCursor(0, 56);
    display.print("<> browse BACK exit");
    return;
  }

  fischPrintNameCentered(bait.name, 13);

  display.setCursor(0, 28);
  display.print("L");
  display.print(bait.luckBonus >= 0 ? "+" : "");
  display.print(bait.luckBonus);
  display.print(" S");
  display.print(bait.lureSpeedBonus >= 0 ? "+" : "");
  display.print(bait.lureSpeedBonus);

  display.setCursor(0, 45);
  if (owned) {
    display.print("Owned");
  } else {
    display.print("$");
    display.print(fischFormatCoins(bait.cost));
  }

  display.setCursor(0, 56);
  if (owned) {
    display.print("<> browse OK equip");
  } else if (fischBaitBrowseShopMode) {
    display.print("<> browse OK buy");
  } else {
    display.print("<> browse BACK exit");
  }
}

void runFischBaitBrowseMenu(bool shopMode, int mapFilter) {
  fischBaitBrowseShopMode = shopMode;
  fischBuildBaitFilter(mapFilter);
  fischBaitBrowseIndex = -1;
  fischTransitionTo(drawFischBaitBrowseFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischBaitBrowseIndex++;
      if (fischBaitBrowseIndex >= fischBaitFilteredCount) fischBaitBrowseIndex = -1;
      drawFischBaitBrowseFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      fischBaitBrowseIndex--;
      if (fischBaitBrowseIndex < -1) fischBaitBrowseIndex = fischBaitFilteredCount - 1;
      drawFischBaitBrowseFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      if (fischBaitBrowseIndex == -1) {
        fischCache.equippedBaitIndex = -1;
        fischSaveSaveData();
        playBeep(50);
        drawFischBaitBrowseFrame();
        display.display();
      } else {
        int realIdx = fischBaitFilteredIndices[fischBaitBrowseIndex];
        if (fischOwnsBait(realIdx)) {
          fischCache.equippedBaitIndex = (int8_t)realIdx;
          fischSaveSaveData();
          playBeep(50);
          drawFischBaitBrowseFrame();
          display.display();
        } else if (fischBaitBrowseShopMode) {
          const FischBaitEntry& shopBait = FISCH_BAITS[realIdx];
          if (fischCache.coins < shopBait.cost) {
            if (displayReady) {
              display.clearDisplay();
              display.setTextSize(1);
              fischPrintCentered("Not enough coins", 28);
              display.display();
            }
            delay(900);
            drawFischBaitBrowseFrame();
            display.display();
          } else if (fischConfirmOverlay("Buy this bait?")) {
            fischCache.coins -= shopBait.cost;
            fischCache.baitOwned[realIdx] = true;
            fischCache.equippedBaitIndex = (int8_t)realIdx;
            fischSaveSaveData();
            Serial.print("[Fisch] ซื้อเหยื่อ: ");
            Serial.println(shopBait.name);
            playBeepPattern(2, 60, 60);
            drawFischBaitBrowseFrame();
            display.display();
          } else {
            drawFischBaitBrowseFrame();
            display.display();
          }
        }
      }
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Shop - Sell Fish (ยืม runFischFishListMenu/runFischItemBrowseMenu มาใช้แบบ sellMode=true) / Buy Rods
// (ยืม runFischRodBrowseMenu มาใช้แบบ shopMode=true) / Buy Bait (ยืม runFischBaitBrowseMenu แบบเดียวกัน)
// ---------------------------------------------------------------------------
const char* const FISCH_SHOP_ITEM_NAMES[] = { "> Sell Fish", "> Buy Rods", "> Buy Bait" };
const int FISCH_SHOP_ITEM_COUNT = 3;

void drawFischShopMenuFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  fischPrintCentered("Shop", 2);

  display.setCursor(0, 16);
  display.print("Coins: ");
  display.print(fischCache.coins);

  fischPrintCentered(FISCH_SHOP_ITEM_NAMES[fischShopMenuIndex], 34);
  fischPrintCentered("<> OK BACK", 56);
}

void runFischShopMenu() {
  fischShopMenuIndex = 0;
  fischTransitionTo(drawFischShopMenuFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischShopMenuIndex = (fischShopMenuIndex + 1) % FISCH_SHOP_ITEM_COUNT;
      drawFischShopMenuFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischShopMenuIndex = (fischShopMenuIndex - 1 + FISCH_SHOP_ITEM_COUNT) % FISCH_SHOP_ITEM_COUNT;
      drawFischShopMenuFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      if (fischShopMenuIndex == 0) {
        runFischFishListMenu(true);
      } else if (fischShopMenuIndex == 1) {
        runFischRodBrowseMenu(true);
      } else {
        runFischBaitBrowseMenu(true, -1); // Shop > Buy Bait โชว์แค่เหยื่อทั่วไป (เฉพาะเกาะซื้อที่ตัวเกาะเอง)
      }
      fischTransitionTo(drawFischShopMenuFrame);
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
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
  display.print("Stats - Lv.");
  display.print(fischCurrentLevel());

  display.setCursor(0, 14);
  display.print("Casts: ");
  display.print(fischCache.totalCasts);
  display.print(" Catch: ");
  display.print(fischCache.totalCatches);

  display.setCursor(0, 26);
  unsigned long attempts = fischCache.totalCatches + fischCache.totalEscapes;
  int rate = attempts > 0 ? (int)(100UL * fischCache.totalCatches / attempts) : 0;
  display.print("Success: ");
  display.print(rate);
  display.print("%");

  display.setCursor(0, 38);
  int level = fischCurrentLevel();
  uint32_t xpAtLevel = fischXpForLevel(level);
  uint32_t xpForNext = fischXpForLevel(level + 1) - xpAtLevel;
  uint32_t xpIntoLevel = fischCache.totalXp - xpAtLevel;
  display.print("XP: ");
  display.print(xpIntoLevel);
  display.print("/");
  display.print(xpForNext);

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
// Debug Mode (item 2, เมนูย่อยแยกต่างหาก - ดู runFischDebugMenu), Back (item 3) - ปิด Momentum มีกวนๆ
// นิดหน่อยด้วย confirm ล้อเล่น (ดู runFischSettingsMenu) แต่เปิดกลับไม่ต้องถามอะไร ไม่มีอะไรเสียหาย
// ---------------------------------------------------------------------------
const char* const FISCH_SETTINGS_ITEMS[] = { "Reel Momentum", "Reset all data", "Debug Mode", "Back" };
const int FISCH_SETTINGS_ITEM_COUNT = 4;

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
          fischResetSaveDataToEmpty();
          fischSaveSaveData();
          Serial.println("[Fisch] ล้างเซฟทั้งหมดแล้ว");
          playBeepPattern(2, 80, 80);
        }
        drawFischSettingsFrame();
        display.display();
      } else if (fischSettingsIndex == 2) {
        runFischDebugMenu();
        fischTransitionTo(drawFischSettingsFrame);
        while (btnOk.isPressed()) { delay(20); }
        btnOk.lastPressed = false;
      } else {
        return;
      }
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Debug Mode - เมนูย่อยใน Settings ไว้ทดสอบของที่ต้อง level/เงินสูงๆโดยไม่ต้องเก็บจริง (Absolute Rod lvl1000/
// $2.5M, เกาะ Farhaven lvl500 ฯลฯ) ทุก toggle เป็น**ตัวแปร runtime ล้วนๆ ไม่บันทึกลง save data เลย** - ปิด
// เกมแล้วเปิดใหม่ค่ากลับเป็นปกติเองอัตโนมัติ (ไม่ต้องกังวลว่าจะลืมปิดแล้วเผลอโกงตอนเล่นจริง) "Reset to
// Default" แค่รีเซ็ต toggle พวกนี้กลับ off ไม่ใช่ล้างเซฟ (นั่นคือ "Reset all data" คนละปุ่มกัน) - ตัวแปร
// fischDebugUnlockAllRods/_AllIslands/_ForceRarity ประกาศไว้ต้นไฟล์แล้ว (ดู fischOwnsRod/fischOwnsMap/
// fischRollRarity ที่เรียกใช้ตั้งแต่ก่อนหน้านี้ในไฟล์)
// ---------------------------------------------------------------------------
const char* const FISCH_DEBUG_ITEMS[] = {
  "Level", "Coins", "Unlock All Rods", "Unlock All Islands", "Force Rarity", "Force Mutation",
  "Reset to Default", "Back"
};
const int FISCH_DEBUG_ITEM_COUNT = 8;
const int FISCH_DEBUG_LEVEL_STEP = 5;
const uint32_t FISCH_DEBUG_COINS_STEP = 5000;

void drawFischDebugFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setCursor(0, 0);
  display.print("Debug Mode");
  display.setCursor(0, 24);
  display.print("> ");
  switch (fischDebugIndex) {
    case 0: display.print(String("Level: ") + fischCurrentLevel()); break;
    case 1: display.print(String("Coins: ") + fischCache.coins); break;
    case 2: display.print(fischDebugUnlockAllRods ? "Unlock All Rods: On" : "Unlock All Rods: Off"); break;
    case 3: display.print(fischDebugUnlockAllIslands ? "Unlock All Islands: On" : "Unlock All Islands: Off"); break;
    case 4: display.print(String("Force Rarity: ") +
                (fischDebugForceRarity < 0 ? "Off" : FISCH_RARITY_NAMES[fischDebugForceRarity])); break;
    case 5: display.print(String("Force Mutation: ") +
                (fischDebugForceMutation < 0 ? "Off" : FISCH_MUTATIONS[fischDebugForceMutation].name)); break;
    default: display.print(FISCH_DEBUG_ITEMS[fischDebugIndex]); break;
  }
  display.setCursor(0, 44);
  display.print(fischDebugIndex <= 1 ? "OK adjust" : "OK toggle/reset");
  display.setCursor(0, 56);
  display.print("<> nav BACK exit");
}

/// โหมดปรับค่าตัวเลข (Level/Coins) - กดค้าง <> ปรับทีละ step เร็วๆ (ดู fischHoldRepeatStep) OK/BACK บันทึก
/// แล้วกลับเมนู Debug ปกติ เปลี่ยน XP ตรงๆผ่าน fischXpForLevel (แปลง level เป้าหมายกลับเป็น XP ที่ตรงพอดี)
void runFischDebugAdjustLevel() {
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischCache.totalXp = fischXpForLevel(fischCurrentLevel() + FISCH_DEBUG_LEVEL_STEP);
      drawFischDebugFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      int newLevel = fischCurrentLevel() - FISCH_DEBUG_LEVEL_STEP;
      if (newLevel < 1) newLevel = 1;
      fischCache.totalXp = fischXpForLevel(newLevel);
      drawFischDebugFrame();
      display.display();
    }
    if (btnOk.consumeJustPressed() || btnBack.consumeJustPressed()) {
      fischSaveSaveData();
      return;
    }
    delay(30);
  }
}

void runFischDebugAdjustCoins() {
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;

  while (true) {
    if (fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat)) {
      fischCache.coins += FISCH_DEBUG_COINS_STEP;
      drawFischDebugFrame();
      display.display();
    }
    if (fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat)) {
      fischCache.coins = (fischCache.coins >= FISCH_DEBUG_COINS_STEP) ? fischCache.coins - FISCH_DEBUG_COINS_STEP : 0;
      drawFischDebugFrame();
      display.display();
    }
    if (btnOk.consumeJustPressed() || btnBack.consumeJustPressed()) {
      fischSaveSaveData();
      return;
    }
    delay(30);
  }
}

void runFischDebugMenu() {
  fischDebugIndex = 0;
  fischTransitionTo(drawFischDebugFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnNext.consumeJustPressed()) {
      fischDebugIndex = (fischDebugIndex + 1) % FISCH_DEBUG_ITEM_COUNT;
      drawFischDebugFrame();
      display.display();
    }
    if (btnPrev.consumeJustPressed()) {
      fischDebugIndex = (fischDebugIndex - 1 + FISCH_DEBUG_ITEM_COUNT) % FISCH_DEBUG_ITEM_COUNT;
      drawFischDebugFrame();
      display.display();
    }
    if (btnBack.consumeJustPressed()) return;
    if (btnOk.consumeJustPressed()) {
      if (fischDebugIndex == 0) {
        runFischDebugAdjustLevel();
      } else if (fischDebugIndex == 1) {
        runFischDebugAdjustCoins();
      } else if (fischDebugIndex == 2) {
        fischDebugUnlockAllRods = !fischDebugUnlockAllRods;
      } else if (fischDebugIndex == 3) {
        fischDebugUnlockAllIslands = !fischDebugUnlockAllIslands;
      } else if (fischDebugIndex == 4) {
        fischDebugForceRarity++;
        if (fischDebugForceRarity > FISCH_LEGENDARY) fischDebugForceRarity = -1; // วนกลับ Off
      } else if (fischDebugIndex == 5) {
        fischDebugForceMutation++;
        if (fischDebugForceMutation >= FISCH_MUTATION_COUNT) fischDebugForceMutation = -1; // วนกลับ Off
      } else if (fischDebugIndex == 6) {
        fischDebugUnlockAllRods = false;
        fischDebugUnlockAllIslands = false;
        fischDebugForceRarity = -1;
        fischDebugForceMutation = -1;
        Serial.println("[Fisch] Debug Mode: รีเซ็ต toggle ทั้งหมดกลับ default แล้ว");
      } else {
        return; // Back
      }
      drawFischDebugFrame();
      display.display();
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Travel - หน้ารายละเอียดเกาะเดียว (เข้าอัตโนมัติจาก runFischTravelMapMenu ตอนเป้านิ่งทับเกาะ) โชว์ระดับ/
// ค่าผ่านทางถ้ายังไม่ปลดล็อก, OK = เดินทาง (ถ้ามีแล้ว) หรือพยายามปลดล็อก (เช็คเลเวล+เงินก่อนเสมอ)
// ---------------------------------------------------------------------------
void drawFischTravelDetailFrame() {
  if (!displayReady) return;
  display.clearDisplay();
  display.setTextSize(1);

  const FischMapEntry& m = FISCH_MAPS[fischTravelDetailIdx];
  bool owned = fischOwnsMap(fischTravelDetailIdx);
  bool isCurrent = (fischCache.currentMapIndex == fischTravelDetailIdx);

  fischPrintNameCentered(m.name, 6);

  if (owned) {
    display.setCursor(0, 28);
    display.print(isCurrent ? "Currently fishing here" : "Unlocked");
    display.setCursor(0, 56);
    // > เปิด Shop เฉพาะเกาะนี้ (ขายเหยื่อธีมเกาะ - ดู runFischTravelIslandDetail) มีทุกเกาะที่ปลดล็อกแล้ว
    display.print(isCurrent ? "> Shop  BACK exit" : "OK travel > Shop");
  } else {
    display.setCursor(0, 28);
    display.print("Requires Lv.");
    display.print(m.levelRequired);
    display.setCursor(0, 38);
    display.print("Cost: ");
    display.print(m.unlockCost);
    display.setCursor(0, 56);
    display.print("OK unlock BACK exit");
  }
}

void runFischTravelIslandDetail(int mapIdx) {
  fischTravelDetailIdx = mapIdx;
  fischTransitionTo(drawFischTravelDetailFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  while (true) {
    if (btnBack.consumeJustPressed()) return;
    if (btnNext.consumeJustPressed() && fischOwnsMap(mapIdx)) {
      runFischBaitBrowseMenu(true, mapIdx); // Shop เฉพาะเกาะนี้ - ขายแค่เหยื่อธีมเกาะนี้เท่านั้น
      fischTransitionTo(drawFischTravelDetailFrame);
      while (btnNext.isPressed()) { delay(20); }
      btnNext.lastPressed = false;
    }
    if (btnOk.consumeJustPressed()) {
      bool owned = fischOwnsMap(mapIdx);
      bool isCurrent = fischCache.currentMapIndex == mapIdx;
      if (owned && !isCurrent) {
        fischCache.currentMapIndex = (uint8_t)mapIdx;
        fischSaveSaveData();
        Serial.print("[Fisch] เดินทางไป: ");
        Serial.println(FISCH_MAPS[mapIdx].name);
        playBeep(60);
        return; // กลับไปหน้าเป้า/แผนที่ ให้เห็นผลลัพธ์ก่อนออกจาก Travel จริงๆ
      } else if (!owned) {
        const FischMapEntry& m = FISCH_MAPS[mapIdx];
        if (fischCurrentLevel() < m.levelRequired) {
          if (displayReady) {
            display.clearDisplay();
            display.setTextSize(1);
            fischPrintCentered("Level too low", 28);
            display.display();
          }
          delay(900);
        } else if (fischCache.coins < m.unlockCost) {
          if (displayReady) {
            display.clearDisplay();
            display.setTextSize(1);
            fischPrintCentered("Not enough coins", 28);
            display.display();
          }
          delay(900);
        } else if (fischConfirmOverlay("Unlock this island?")) {
          fischCache.coins -= m.unlockCost;
          fischCache.mapUnlocked[mapIdx] = true;
          fischCache.currentMapIndex = (uint8_t)mapIdx;
          fischSaveSaveData();
          Serial.print("[Fisch] ปลดล็อกเกาะ: ");
          Serial.println(m.name);
          playBeepPattern(2, 60, 60);
        }
      }
      drawFischTravelDetailFrame();
      display.display();
      while (btnOk.isPressed()) { delay(20); }
      btnOk.lastPressed = false;
    }
    delay(30);
  }
}

// ---------------------------------------------------------------------------
// Travel map - เป้า (reticle) ขยับข้ามจอ 2 แกนด้วยปุ่มแค่ 3 ปุ่ม (Prev/Next/OK): กด < > เฉยๆ = แกน X, กด
// SW_OK ค้างแล้วกด < > = แกน Y (ยืมปุ่มเดิมมาใช้ 2 หน้าที่ผ่านตัวปรับ ไม่ต้องมีปุ่มใหม่) พอเป้าทับไอคอนเกาะ
// ไหนอยู่ กด SW_OK เฉยๆ (กด-ปล่อยไม่มีกด < > คั่นกลาง) เพื่อเข้าหน้าเกาะนั้น (ดู runFischTravelMapMenu ว่า
// แยกอย่างไรระหว่าง "กด OK ค้างเป็นตัวปรับแกน" กับ "กด OK ยืนยัน") ไอคอนสามแบบ: กรอบเปล่า = เกาะที่อยู่
// ตอนนี้, ทึบ = ปลดล็อกแล้วแต่ไม่ได้อยู่, วงกลมกลวง = ยังไม่ปลดล็อก
// ---------------------------------------------------------------------------
/// อ่านบิตเดียวของพื้นหลังแมพโลกที่พิกัด (wx,wy) - นอกขอบเขต 0..FISCH_WORLD_MAP_SIZE-1 ถือว่าเป็นน้ำ/ว่าง
/// (false) แพ็คบิตแบบเดียวกับ FISCH_LOADING_FRAME_x (idx=y*W+x, byte=idx/8, bit=idx%8, LSB-first)
bool fischWorldBitAt(int wx, int wy) {
  if (wx < 0 || wy < 0 || wx >= FISCH_WORLD_MAP_SIZE || wy >= FISCH_WORLD_MAP_SIZE) return false;
  int idx = wy * FISCH_WORLD_MAP_SIZE + wx;
  uint8_t b = pgm_read_byte(&FISCH_WORLD_MAP_BITMAP[idx / 8]);
  return (b >> (idx % 8)) & 1;
}

void drawFischTravelMapFrame() {
  if (!displayReady) return;
  display.clearDisplay();

  // กล้องมองจากบนลงล่างตามหัวผู้เล่น: เป้าอยู่กลางจอตายตัวเสมอ ตัวแมพ (พื้นหลัง+เกาะทุกเกาะ) ต่างหากที่
  // เลื่อนใต้เป้าแทน - แปลงพิกัดโลกเป็นพิกัดจอโดยลบด้วย offset กล้อง (= ตำแหน่งเป้าในโลก - จุดกึ่งกลางจอ)
  int camOffX = fischTravelReticleX - FISCH_CX;
  int camOffY = fischTravelReticleY - FISCH_CY;

  // พื้นหลังภูมิประเทศ - สุ่มพิกเซลทีละจุดจากหน้าต่างกล้อง (ครอปจากบิตแมพโลก 500x500 ที่วาดเอง) วาดก่อน
  // ที่จะทับด้วยเกาะ/เป้าด้านล่าง
  for (int sy = 0; sy < SCREEN_HEIGHT; sy++) {
    for (int sx = 0; sx < SCREEN_WIDTH; sx++) {
      if (fischWorldBitAt(sx + camOffX, sy + camOffY)) {
        display.drawPixel(sx, sy, SSD1306_WHITE);
      }
    }
  }

  // พื้นหลังภูมิประเทศอาจมีจุดขาวทับตัวหนังสือได้ (อ่านไม่ออก) กันไว้ด้วยแถบดำทึบก่อนพิมพ์ข้อความเสมอ
  display.fillRect(0, 0, SCREEN_WIDTH, 8, SSD1306_BLACK);
  display.setCursor(0, 0);
  display.print("Travel");

  for (int m = 0; m < FISCH_MAP_COUNT; m++) {
    int ix = FISCH_MAPS[m].x - camOffX;
    int iy = FISCH_MAPS[m].y - camOffY;
    if (ix < -8 || ix > SCREEN_WIDTH + 8 || iy < -8 || iy > SCREEN_HEIGHT + 8) continue; // นอกจอ ไม่ต้องวาด
    if (m == fischCache.currentMapIndex) {
      display.drawRect(ix - 4, iy - 4, 8, 8, SSD1306_WHITE);
    } else if (fischOwnsMap(m)) {
      display.fillRect(ix - 3, iy - 3, 6, 6, SSD1306_WHITE);
    } else {
      display.drawCircle(ix, iy, 3, SSD1306_WHITE);
    }
  }

  // เป้า (กากบาทเล็กๆ) - อยู่กลางจอตายตัวเสมอ ไม่ขยับตามพิกัดโลกอีกต่อไป
  display.drawFastHLine(FISCH_CX - 3, FISCH_CY, 7, SSD1306_WHITE);
  display.drawFastVLine(FISCH_CX, FISCH_CY - 3, 7, SSD1306_WHITE);

  if (fischTravelLastNear >= 0) {
    display.fillRect(0, 45, SCREEN_WIDTH, 9, SSD1306_BLACK);
    fischPrintNameCentered(FISCH_MAPS[fischTravelLastNear].name, 46);
  }

  display.fillRect(0, 56, SCREEN_WIDTH, 8, SSD1306_BLACK);
  display.setCursor(0, 56);
  // ตัวเตือนปุ่มเปลี่ยนตามบริบท - เต็มพอดีทั้งคู่ ไม่ต้องยัดสองอย่างพร้อมกันจนแน่นเกิน
  display.print(fischTravelLastNear >= 0 ? "OK enter  BACK exit" : "<>:X OK+<>:Y BACK");
}

void runFischTravelMapMenu() {
  fischTravelReticleX = FISCH_MAPS[fischCache.currentMapIndex].x;
  fischTravelReticleY = FISCH_MAPS[fischCache.currentMapIndex].y;
  fischTravelLastNear = -1;
  fischTransitionTo(drawFischTravelMapFrame);
  while (btnBack.isPressed() || btnOk.isPressed()) { delay(20); }
  btnBack.lastPressed = false;
  btnOk.lastPressed = false;

  unsigned long nextHoldStart = 0, nextLastRepeat = 0;
  unsigned long prevHoldStart = 0, prevLastRepeat = 0;
  const int FISCH_TRAVEL_STEP = 3;
  const int FISCH_TRAVEL_NEAR_RADIUS_SQ = 64; // รัศมี 8px (8*8)

  // เดิมใช้ "เป้านิ่งทับเกาะสักพักแล้วเข้าเอง" แต่ผู้ใช้ขอกลับไปใช้กด OK ยืนยันแทน - ปัญหาคือ SW_OK ถูกใช้
  // เป็นตัวปรับแกน Y ไปแล้ว (ค้าง OK แล้วกด < >) ต้องแยกให้ออกว่า "กด OK ค้าง" ครั้งนี้ถูกใช้เป็นตัวปรับแกน
  // ไปแล้วหรือเปล่า - ถ้าใช่ ตอนปล่อยจะไม่นับเป็นการกดยืนยัน ถ้าไม่เคยใช้เป็นตัวปรับเลยตลอดการกดครั้งนั้น
  // (กด-ปล่อยเฉยๆ ไม่มีการกด < > คั่นกลาง) ตอนปล่อยถึงจะนับเป็นกดยืนยันเข้าเกาะ (ถ้าตอนนั้นเป้าทับเกาะอยู่)
  bool okWasHeldLastFrame = false;
  bool okUsedAsAxisModifier = false;

  while (true) {
    bool okHeld = btnOk.isPressed();
    if (okHeld && !okWasHeldLastFrame) okUsedAsAxisModifier = false; // OK เพิ่งถูกกด - เริ่มนับรอบใหม่

    bool stepNext = fischHoldRepeatStep(btnNext, nextHoldStart, nextLastRepeat);
    bool stepPrev = fischHoldRepeatStep(btnPrev, prevHoldStart, prevLastRepeat);
    bool moved = false;
    if (stepNext) {
      if (okHeld) { fischTravelReticleY += FISCH_TRAVEL_STEP; okUsedAsAxisModifier = true; }
      else { fischTravelReticleX += FISCH_TRAVEL_STEP; }
      moved = true;
    }
    if (stepPrev) {
      if (okHeld) { fischTravelReticleY -= FISCH_TRAVEL_STEP; okUsedAsAxisModifier = true; }
      else { fischTravelReticleX -= FISCH_TRAVEL_STEP; }
      moved = true;
    }
    // ขอบเขตตอนนี้คือขอบโลกเสมือน (0..500) ไม่ใช่ขอบจออีกต่อไป เพราะเป้าอยู่กลางจอตายตัว ตัวแมพเลื่อนแทน
    if (fischTravelReticleX < 0) fischTravelReticleX = 0;
    if (fischTravelReticleX > FISCH_TRAVEL_WORLD_SIZE) fischTravelReticleX = FISCH_TRAVEL_WORLD_SIZE;
    if (fischTravelReticleY < 0) fischTravelReticleY = 0;
    if (fischTravelReticleY > FISCH_TRAVEL_WORLD_SIZE) fischTravelReticleY = FISCH_TRAVEL_WORLD_SIZE;

    if (btnBack.consumeJustPressed()) return;

    int near = -1;
    for (int m = 0; m < FISCH_MAP_COUNT; m++) {
      int dx = fischTravelReticleX - FISCH_MAPS[m].x;
      int dy = fischTravelReticleY - FISCH_MAPS[m].y;
      if (dx * dx + dy * dy <= FISCH_TRAVEL_NEAR_RADIUS_SQ) { near = m; break; }
    }
    bool nearChanged = (near != fischTravelLastNear);
    fischTravelLastNear = near;

    if (moved || nearChanged) {
      drawFischTravelMapFrame();
      display.display();
    }

    // OK เพิ่งถูกปล่อย (falling edge) และไม่เคยถูกใช้เป็นตัวปรับแกน Y เลยตลอดการกดค้างครั้งนี้ = กดยืนยัน
    if (!okHeld && okWasHeldLastFrame && !okUsedAsAxisModifier && near >= 0) {
      runFischTravelIslandDetail(near);
      fischTravelLastNear = -1;
      fischTransitionTo(drawFischTravelMapFrame);
      while (btnBack.isPressed()) { delay(20); }
      btnBack.lastPressed = false;
    }
    okWasHeldLastFrame = okHeld;

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
