#ifndef OTHERS_GAMES_ARCADE_H
#define OTHERS_GAMES_ARCADE_H

// ============================================================================
// others_games_arcade.h - เกมแนว reflex/arcade ในเมนู Others: Mini Dino, Pong, DVD Bounce, Flappy
// Block (ดู runOthersMenu ใน .ino หลัก) แยกออกมาจาก RemoteLockKnob.ino ตอนแยก Others ออกจากฟีเจอร์
// remote lock หลัก (ไฟล์ .ino เดิมโตจน 77% เป็นเนื้อหา Others ล้วนๆ)
//
// ข้อควรรู้ก่อนแก้ไฟล์นี้ (เหมือน fisch_game.h ทุกอย่าง): ฟังก์ชันในไฟล์ .h ที่ #include เข้ามาใน .ino
// จะ**ไม่ถูก**สแกนหา prototype อัตโนมัติแบบเดียวกับฟังก์ชันที่เขียนตรงใน .ino เอง (auto-prototype ของ
// Arduino สแกนแค่เนื้อไฟล์ .ino ดิบๆ ไม่มองเข้าไปใน #include) เพราะงั้นฟังก์ชันในไฟล์นี้ที่ถูกเรียกก่อน
// จุดที่ตัวเองถูกประกาศ (ทั้งจากในไฟล์นี้เอง และจาก runOthersMenu() ใน others_menu.h) ต้องมี forward
// declaration ไว้เป็นก้อนเดียวด้านล่างก่อนเนื้อหาจริงเสมอ - เพิ่มฟังก์ชันใหม่ = ต้องเพิ่ม prototype ในก้อน
// นั้นด้วย ส่วนฟังก์ชัน/ตัวแปร global ที่ประกาศตรงใน .ino เอง (display, btnOk, playBeep, Preferences,
// SCREEN_WIDTH/HEIGHT ฯลฯ) เรียกจากที่นี่ได้ตามปกติ ไม่ต้อง forward declare เพราะ auto-prototype ของ
// Arduino จัดการให้แล้ว - ไฟล์นี้ต้อง #include อยู่ก่อน others_menu.h (ที่มี runOthersMenu เรียกเข้ามา)
// ============================================================================

// ---------------------------------------------------------------------------
// FORWARD DECLARATIONS
// ---------------------------------------------------------------------------
bool checkDinoCollision(int jumpOffsetPx, int obstacleXPx);
void drawDinoFrame(int jumpOffsetPx, int obstacleXPx, unsigned int score, unsigned int highScore, bool gameOver);
void runMiniDinoGame();
void runDvdBounce();
void runPong();
void drawFlappyFrame(float blockY, float pipeX, int gapY, unsigned int score, unsigned int highScore, bool gameOver);
bool checkFlappyCollision(float blockY, float pipeX, int gapY);
void runFlappyBlock();

// ค่าคงที่ของเกม Mini Dino (ดู runMiniDinoGame) - ปรับความยากง่าย/ขนาดได้ตรงนี้ที่เดียว
const int DINO_X = 10;          // ตำแหน่ง X คงที่ของไดโน (ขยับแค่แนวตั้งตอนกระโดด)
const int DINO_SIZE = 8;        // สี่เหลี่ยมจัตุรัส 8x8 พิกเซล
const int DINO_GROUND_Y = 56;   // เส้นพื้น (baseline) - เผื่อที่ด้านบนไว้โชว์คะแนน/high score
const int OBSTACLE_WIDTH = 6;
const int OBSTACLE_HEIGHT = 12;
// ความเร็ว obstacle ไม่คงที่แล้ว - เริ่มที่ BASE แล้วเพิ่มทีละ 1 ทุกๆ SCORE_DIVISOR แต้ม จนถึง MAX (กัน
// เร็วจนเกมเล่นไม่ได้จริง/ชนตรวจจับพลาด) ดู runMiniDinoGame ตรงคำนวณ currentSpeed แต่ละ tick
const int OBSTACLE_SPEED_BASE = 3;
const int OBSTACLE_SPEED_MAX = 8;
const int OBSTACLE_SPEED_SCORE_DIVISOR = 100; // score ~10/วินาที เลยแปลว่าเร็วขึ้นทุกๆ ~10 วิที่รอด
const unsigned long DINO_TICK_MS = 40;    // ~25 fps
const float DINO_JUMP_VELOCITY = 4.5;     // ความเร็วเริ่มกระโดด (บวก = ขึ้น)
const float DINO_GRAVITY = 0.35;          // แรงดึงกลับลงต่อ tick

/// เช็คชนแบบ AABB ธรรมดา - ไดโนอยู่ X คงที่ (แค่ขยับแนวตั้งตาม jumpOffsetPx), obstacle ขยับแนวนอนอย่างเดียว
bool checkDinoCollision(int jumpOffsetPx, int obstacleXPx) {
  int dinoTop = DINO_GROUND_Y - DINO_SIZE - jumpOffsetPx;
  // บั๊กเดิม: เคยตั้งเป็น DINO_GROUND_Y ตรงๆ (ไม่ลบ jumpOffsetPx) ทำให้ hitbox ยืดยาวจากตัวไดโนลงไป
  // ถึงพื้นเสมอไม่ว่าจะกระโดดสูงแค่ไหน กลายเป็นชนจากแนวนอน (X) เพียงอย่างเดียวเป็นตัวตัดสิน กระโดดพ้น
  // สิ่งกีดขวางจริงบนจอแต่ยังโดนนับว่าชนอยู่ดี - แก้โดยลบ jumpOffsetPx ออกให้ตรงกับ dinoTop จริงๆ
  int dinoBottom = DINO_GROUND_Y - jumpOffsetPx;
  int dinoLeft = DINO_X;
  int dinoRight = DINO_X + DINO_SIZE;

  int obsTop = DINO_GROUND_Y - OBSTACLE_HEIGHT;
  int obsBottom = DINO_GROUND_Y;
  int obsLeft = obstacleXPx;
  int obsRight = obstacleXPx + OBSTACLE_WIDTH;

  bool overlapX = dinoRight > obsLeft && dinoLeft < obsRight;
  bool overlapY = dinoBottom > obsTop && dinoTop < obsBottom;
  return overlapX && overlapY;
}

void drawDinoFrame(int jumpOffsetPx, int obstacleXPx, unsigned int score, unsigned int highScore, bool gameOver) {
  if (!displayReady) return;

  display.clearDisplay();

  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("HI ");
  display.print(highScore);
  display.setCursor(76, 0);
  display.print("SC ");
  display.print(score);

  display.drawFastHLine(0, DINO_GROUND_Y + 1, SCREEN_WIDTH, SSD1306_WHITE);

  int dinoTop = DINO_GROUND_Y - DINO_SIZE - jumpOffsetPx;
  display.fillRect(DINO_X, dinoTop, DINO_SIZE, DINO_SIZE, SSD1306_WHITE);

  int obsTop = DINO_GROUND_Y - OBSTACLE_HEIGHT;
  display.fillRect(obstacleXPx, obsTop, OBSTACLE_WIDTH, OBSTACLE_HEIGHT, SSD1306_WHITE);

  if (gameOver) {
    display.setTextSize(1);
    display.setCursor(22, 24);
    display.println("GAME OVER");
    display.setCursor(4, 36);
    display.println("OK=retry BACK=exit");
  }

  display.display();
}

/// เกมไดโนกระโดดขั้นต่ำ (มีแค่กระโดด ไม่มีย่อ ตามที่ขอ) - กด < กระโดด (ครั้งเดียวตอนอยู่พื้นเท่านั้น
/// กดค้างตอนลอยอยู่ไม่มีผล), BACK ออกได้ทุกเมื่อแม้กำลังเล่นอยู่ (กลับไป Others menu ทันที ไม่ต้องรอ
/// จบเกมก่อน) คะแนนสูงสุด (high score) เก็บถาวรผ่าน Preferences/NVS flash - รอดข้าม reboot/ไฟดับ
/// ต่างจากตัวแปรใน RAM ธรรมดาที่หายทุกครั้งที่ไฟดับ (บอร์ดพวกนี้ไม่มี disk ให้เขียนแบบคอมทั่วไป)
void runMiniDinoGame() {
  Preferences prefs;
  prefs.begin("dino", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  // รอปล่อยปุ่มทุกปุ่มที่อาจมีผลในเกมก่อนเริ่ม กัน edge การกดที่เพิ่งใช้เลือกเมนูนี้ถูกตีความเป็น
  // กระโดด/ยืนยันซ้ำทันทีที่เข้าเกม
  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;

  Serial.println("[Dino] เข้าเกม Mini Dino");

  while (true) {
    // --- รอบเล่นใหม่หนึ่งรอบ ---
    float jumpOffset = 0;
    float jumpVelocity = 0;
    bool isJumping = false;
    float obstacleX = SCREEN_WIDTH;
    unsigned int score = 0;
    bool gameOver = false;
    unsigned long lastTickMs = millis();
    unsigned long lastScoreMs = millis();

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Dino] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      // เรียก consumeJustPressed() ของทั้งสองปุ่มแยกกันก่อน "เสมอ" ไม่ใช้ || แบบ short-circuit ตรงๆ
      // เพราะ short-circuit จะข้ามการอัปเดต state ของปุ่มที่สองไปถ้าปุ่มแรก true อยู่แล้ว ทำให้
      // edge-detection ของปุ่มที่ถูกข้ามเพี้ยนได้ในรอบถัดไป (เดายังว่ากด/ปล่อยพลาดจังหวะ)
      bool jumpFromPrev = btnPrev.consumeJustPressed();
      bool jumpFromOk = btnOk.consumeJustPressed();
      if ((jumpFromPrev || jumpFromOk) && !isJumping) {
        isJumping = true;
        jumpVelocity = DINO_JUMP_VELOCITY;
        playBeep(40); // กระโดด - บี๊บสั้นเดียว
      }

      if (millis() - lastTickMs >= DINO_TICK_MS) {
        lastTickMs = millis();

        if (isJumping) {
          jumpOffset += jumpVelocity;
          jumpVelocity -= DINO_GRAVITY;
          if (jumpOffset <= 0) {
            jumpOffset = 0;
            isJumping = false;
            jumpVelocity = 0;
          }
        }

        int currentSpeed = OBSTACLE_SPEED_BASE + (int)(score / OBSTACLE_SPEED_SCORE_DIVISOR);
        if (currentSpeed > OBSTACLE_SPEED_MAX) currentSpeed = OBSTACLE_SPEED_MAX;
        obstacleX -= currentSpeed;
        if (obstacleX < -OBSTACLE_WIDTH) {
          // บั๊กเดิม: สุ่มระยะห่างเป็น "พิกเซล" คงที่ (20-80) ไม่ว่าความเร็วจะเท่าไหร่ - พอความเร็วเพิ่ม
          // ตามคะแนน ระยะทางเท่าเดิมแต่วิ่งเร็วขึ้น เวลาจริงที่มีให้ตอบสนองต่อ obstacle แต่ละตัวเลยสั้นลง
          // เรื่อยๆ จนเล่นไม่ทันจริงๆ (ผู้ใช้รายงานว่าตายไม่เกิน ~500 คะแนนเสมอ) แก้โดย scale ระยะห่างตาม
          // currentSpeed ด้วย ให้ "เวลา" ระหว่าง obstacle แต่ละตัวใกล้เคียงเดิมไม่ว่าความเร็วเท่าไหร่
          int gapMin = 20 * currentSpeed / OBSTACLE_SPEED_BASE;
          int gapMax = 80 * currentSpeed / OBSTACLE_SPEED_BASE;
          obstacleX = SCREEN_WIDTH + random(gapMin, gapMax);
        }

        if (millis() - lastScoreMs >= 100) {
          lastScoreMs = millis();
          score++;
        }

        if (checkDinoCollision((int)jumpOffset, (int)obstacleX)) {
          gameOver = true;
          Serial.print("[Dino] ชน - คะแนน: ");
          Serial.println(score);
          if (score > highScore) {
            highScore = score;
            prefs.putUInt("highscore", highScore);
            Serial.println("[Dino] สถิติใหม่! เซฟลง NVS แล้ว");
            playBeepPattern(3, 60, 60); // สถิติใหม่ - บี๊บถี่ 3 ครั้งแทนเสียงสูง (ปรับ pitch ไม่ได้แล้ว)
          } else {
            playBeep(250); // ชนธรรมดา - บี๊บยาวครั้งเดียว
          }
        }

        drawDinoFrame((int)jumpOffset, (int)obstacleX, score, highScore, false);
      }

      delay(5);
    }

    drawDinoFrame((int)jumpOffset, (int)obstacleX, score, highScore, true);

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    // รอผู้เล่นตัดสินใจ: OK = เล่นใหม่ (วนกลับไป while(true) ด้านบน), BACK = ออกไป Others menu
    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Dino] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Dino] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

/// DVD logo เด้งชนขอบจอ - ไม่มี interaction เลยนอกจาก BACK ออกกลับเมนู Others ทันที (ปล่อยดูเพลินๆ)
void runDvdBounce() {
  while (btnBack.isPressed()) { delay(20); }
  btnBack.lastPressed = false;

  Serial.println("[DVD] เข้า DVD Bounce");

  const int BOX_W = 20;
  const int BOX_H = 10;
  float x = 10, y = 10;
  float vx = 1.6, vy = 1.1;
  unsigned long lastTickMs = millis();

  while (true) {
    if (btnBack.consumeJustPressed()) {
      Serial.println("[DVD] ออก (กด BACK)");
      return;
    }

    if (millis() - lastTickMs >= 30) {
      lastTickMs = millis();
      x += vx;
      y += vy;
      if (x <= 0) { x = 0; vx = -vx; }
      if (x + BOX_W >= SCREEN_WIDTH) { x = SCREEN_WIDTH - BOX_W; vx = -vx; }
      if (y <= 0) { y = 0; vy = -vy; }
      if (y + BOX_H >= SCREEN_HEIGHT) { y = SCREEN_HEIGHT - BOX_H; vy = -vy; }

      if (displayReady) {
        display.clearDisplay();
        display.drawRect((int)x, (int)y, BOX_W, BOX_H, SSD1306_WHITE);
        display.setTextSize(1);
        display.setCursor((int)x + 2, (int)y + 1);
        display.print("DVD");
        display.display();
      }
    }
    delay(5);
  }
}

// ค่าคงที่ของ Pong - ไม้อยู่ซ้ายสุด, ลูกบอลเด้งกำแพงขวา/บน/ล่าง พลาดไม้ (หลุดซ้าย) = จบเกม
const int PONG_PADDLE_HEIGHT = 16;
const int PONG_PADDLE_WIDTH = 2;
const int PONG_PADDLE_X = 4;
const float PONG_PADDLE_SPEED = 2.2; // เพิ่มจาก 1.0 ตามที่ขอ ให้ไม้ขยับไวขึ้น
const int PONG_BALL_SIZE = 3;

/// Pong กระทบกำแพง (single-player) - ไม้ควบคุมด้วย < (ขึ้น) / > (ลง) กดค้างขยับต่อเนื่องได้ (poll
/// isPressed() ทุก loop ไม่ใช่ edge-trigger) ลูกบอลเด้งกำแพงบน/ล่าง/ขวา หลุดออกทางซ้าย = จบเกม คะแนน =
/// จำนวนครั้งที่ตีโดนไม้ high score เก็บถาวรผ่าน NVS เหมือน Mini Dino
void runPong() {
  Preferences prefs;
  prefs.begin("pong", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed() || btnNext.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;
  btnNext.lastPressed = false;

  Serial.println("[Pong] เข้าเกม Pong");

  while (true) {
    float paddleY = (SCREEN_HEIGHT - PONG_PADDLE_HEIGHT) / 2.0;
    float ballX = SCREEN_WIDTH / 2.0;
    float ballY = SCREEN_HEIGHT / 2.0;
    // สุ่มทิศเริ่มต้นทุกรอบใหม่ (ทั้งซ้าย/ขวา และมุมขึ้น/ลง) ไม่ใช่ทิศตายตัวเหมือนเดิม - สุ่มขนาด
    // ความเร็วในช่วงแคบๆ ด้วยเผื่อความหลากหลาย ไม่ใช้ sin/cos ให้ยุ่งยาก แค่สุ่มเครื่องหมาย+ขนาดตรงๆ
    float ballVX = (random(2) == 0 ? 1.0 : -1.0) * (1.8 + random(0, 8) / 10.0);
    float ballVY = (random(2) == 0 ? 1.0 : -1.0) * (0.8 + random(0, 8) / 10.0);
    unsigned int score = 0;
    bool gameOver = false;
    unsigned long lastTickMs = millis();

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Pong] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      if (btnPrev.isPressed()) paddleY -= PONG_PADDLE_SPEED;
      if (btnNext.isPressed()) paddleY += PONG_PADDLE_SPEED;
      if (paddleY < 0) paddleY = 0;
      if (paddleY > SCREEN_HEIGHT - PONG_PADDLE_HEIGHT) paddleY = SCREEN_HEIGHT - PONG_PADDLE_HEIGHT;

      if (millis() - lastTickMs >= 25) {
        lastTickMs = millis();
        ballX += ballVX;
        ballY += ballVY;

        if (ballY <= 0) { ballY = 0; ballVY = -ballVY; }
        if (ballY + PONG_BALL_SIZE >= SCREEN_HEIGHT) { ballY = SCREEN_HEIGHT - PONG_BALL_SIZE; ballVY = -ballVY; }
        if (ballX + PONG_BALL_SIZE >= SCREEN_WIDTH) { ballX = SCREEN_WIDTH - PONG_BALL_SIZE; ballVX = -ballVX; }

        // เช็คไม้เฉพาะตอนลูกกำลังวิ่งเข้าหา (vx < 0) กันนับซ้ำหลังเด้งแล้ว (ดู comment หัวไฟล์เรื่อง
        // การเช็ค collision ทีละเฟรมของ Mini Dino - หลักการเดียวกัน)
        if (ballVX < 0 && ballX <= PONG_PADDLE_X + PONG_PADDLE_WIDTH) {
          if (ballY + PONG_BALL_SIZE >= paddleY && ballY <= paddleY + PONG_PADDLE_HEIGHT) {
            ballX = PONG_PADDLE_X + PONG_PADDLE_WIDTH + 1;
            ballVX = -ballVX;
            score++;
          } else if (ballX < 0) {
            gameOver = true;
            Serial.print("[Pong] พลาด - คะแนน: ");
            Serial.println(score);
            if (score > highScore) {
              highScore = score;
              prefs.putUInt("highscore", highScore);
              Serial.println("[Pong] สถิติใหม่! เซฟลง NVS แล้ว");
            }
          }
        }

        if (displayReady) {
          display.clearDisplay();
          display.setTextSize(1);
          display.setCursor(0, 0);
          display.print("HI ");
          display.print(highScore);
          display.setCursor(90, 0);
          display.print("SC ");
          display.print(score);
          display.fillRect(PONG_PADDLE_X, (int)paddleY, PONG_PADDLE_WIDTH, PONG_PADDLE_HEIGHT, SSD1306_WHITE);
          display.fillRect((int)ballX, (int)ballY, PONG_BALL_SIZE, PONG_BALL_SIZE, SSD1306_WHITE);
          display.display();
        }
      }
      delay(5);
    }

    if (displayReady) {
      display.setTextSize(1);
      display.setCursor(28, 26);
      display.print("GAME OVER");
      display.setCursor(4, 38);
      display.print("OK=retry BACK=exit");
      display.display();
    }

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Pong] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Pong] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

// ค่าคงที่ของ Flappy Block ("นก" เป็นสี่เหลี่ยมเฉยๆ เพราะไม่ได้วาดรูปนกจริง) ไปป์เดียวบนจอเหมือน Mini
// Dino (obstacle เดียว ไม่ใช่หลายอันพร้อมกัน)
const int FLAPPY_BLOCK_X = 20;
const int FLAPPY_BLOCK_SIZE = 8;
const float FLAPPY_GRAVITY = 0.35;
const float FLAPPY_FLAP_VELOCITY = -3.2;
const int FLAPPY_PIPE_WIDTH = 8; // ลดจาก 14 - ผู้เล่นรายงานว่าไปป์ดูหนาไปหน่อย เล่นยาก
const int FLAPPY_PIPE_GAP = 26;
const int FLAPPY_PIPE_SPEED = 2;
const unsigned long FLAPPY_TICK_MS = 40;

void drawFlappyFrame(float blockY, float pipeX, int gapY, unsigned int score, unsigned int highScore, bool gameOver) {
  if (!displayReady) return;

  display.clearDisplay();

  display.setTextSize(1);
  display.setCursor(0, 0);
  display.print("HI ");
  display.print(highScore);
  display.setCursor(90, 0);
  display.print("SC ");
  display.print(score);

  display.fillRect((int)pipeX, 0, FLAPPY_PIPE_WIDTH, gapY, SSD1306_WHITE);
  display.fillRect((int)pipeX, gapY + FLAPPY_PIPE_GAP, FLAPPY_PIPE_WIDTH, SCREEN_HEIGHT - (gapY + FLAPPY_PIPE_GAP), SSD1306_WHITE);

  display.fillRect(FLAPPY_BLOCK_X, (int)blockY, FLAPPY_BLOCK_SIZE, FLAPPY_BLOCK_SIZE, SSD1306_WHITE);

  if (gameOver) {
    display.fillRect(24, 24, 80, 16, SSD1306_BLACK);
    display.drawRect(24, 24, 80, 16, SSD1306_WHITE);
    display.setTextSize(1);
    display.setCursor(30, 29);
    display.print("GAME OVER");
  }

  display.display();
}

bool checkFlappyCollision(float blockY, float pipeX, int gapY) {
  int blockTop = (int)blockY;
  int blockBottom = blockTop + FLAPPY_BLOCK_SIZE;
  if (blockTop <= 0 || blockBottom >= SCREEN_HEIGHT) return true; // ชนพื้น/เพดาน

  int pipeLeft = (int)pipeX;
  int pipeRight = pipeLeft + FLAPPY_PIPE_WIDTH;
  int blockLeft = FLAPPY_BLOCK_X;
  int blockRight = FLAPPY_BLOCK_X + FLAPPY_BLOCK_SIZE;

  bool overlapX = blockRight > pipeLeft && blockLeft < pipeRight;
  if (!overlapX) return false;

  bool inGap = blockTop >= gapY && blockBottom <= gapY + FLAPPY_PIPE_GAP;
  return !inGap;
}

/// Flappy Block - กด < หรือ SW_OK "กระพือ" (ดันขึ้นทันที ไม่ใช่กระโดดครั้งเดียวจากพื้นแบบ Dino - กระพือ
/// กลางอากาศซ้ำได้เรื่อยๆ) หลบช่องว่างของไปป์ที่ไหลเข้ามา คะแนน = จำนวนไปป์ที่ผ่านไปได้ high score เก็บ
/// ถาวรผ่าน NVS เหมือน Dino/Pong BACK ออกได้ทุกเมื่อ
void runFlappyBlock() {
  Preferences prefs;
  prefs.begin("flappy", false);
  unsigned int highScore = prefs.getUInt("highscore", 0);

  while (btnOk.isPressed() || btnBack.isPressed() || btnPrev.isPressed()) { delay(20); }
  btnOk.lastPressed = false;
  btnBack.lastPressed = false;
  btnPrev.lastPressed = false;

  Serial.println("[Flappy] เข้าเกม Flappy Block");

  while (true) {
    float blockY = SCREEN_HEIGHT / 2.0;
    float blockVY = 0;
    float pipeX = SCREEN_WIDTH;
    int gapY = random(6, SCREEN_HEIGHT - FLAPPY_PIPE_GAP - 6);
    unsigned int score = 0;
    bool scoredThisPipe = false;
    bool gameOver = false;
    unsigned long lastTickMs = millis();

    drawFlappyFrame(blockY, pipeX, gapY, score, highScore, false);

    while (!gameOver) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Flappy] ออกจากเกม (กด BACK)");
        prefs.end();
        return;
      }

      // เหมือน Dino เป๊ะ: เรียก consumeJustPressed() ทั้งสองปุ่มแยกกันก่อนเสมอ ไม่ใช้ || แบบ
      // short-circuit กัน edge-detection ของปุ่มที่ถูกข้ามเพี้ยน
      bool flapFromPrev = btnPrev.consumeJustPressed();
      bool flapFromOk = btnOk.consumeJustPressed();
      if (flapFromPrev || flapFromOk) {
        blockVY = FLAPPY_FLAP_VELOCITY;
        playBeep(20);
      }

      if (millis() - lastTickMs >= FLAPPY_TICK_MS) {
        lastTickMs = millis();

        blockY += blockVY;
        blockVY += FLAPPY_GRAVITY;

        pipeX -= FLAPPY_PIPE_SPEED;
        if (!scoredThisPipe && pipeX + FLAPPY_PIPE_WIDTH < FLAPPY_BLOCK_X) {
          score++;
          scoredThisPipe = true;
        }
        if (pipeX < -FLAPPY_PIPE_WIDTH) {
          pipeX = SCREEN_WIDTH;
          gapY = random(6, SCREEN_HEIGHT - FLAPPY_PIPE_GAP - 6);
          scoredThisPipe = false;
        }

        if (checkFlappyCollision(blockY, pipeX, gapY)) {
          gameOver = true;
          Serial.print("[Flappy] ชน - คะแนน: ");
          Serial.println(score);
          if (score > highScore) {
            highScore = score;
            prefs.putUInt("highscore", highScore);
            Serial.println("[Flappy] สถิติใหม่! เซฟลง NVS แล้ว");
            playBeepPattern(3, 60, 60);
          } else {
            playBeep(250);
          }
        }

        drawFlappyFrame(blockY, pipeX, gapY, score, highScore, false);
      }
      delay(5);
    }

    drawFlappyFrame(blockY, pipeX, gapY, score, highScore, true);

    while (btnOk.isPressed() || btnBack.isPressed()) { delay(20); }
    btnOk.lastPressed = false;
    btnBack.lastPressed = false;

    while (true) {
      if (btnBack.consumeJustPressed()) {
        Serial.println("[Flappy] ออกจากเกมหลังจบรอบ (กด BACK)");
        prefs.end();
        return;
      }
      if (btnOk.consumeJustPressed()) {
        Serial.println("[Flappy] เล่นใหม่อีกรอบ");
        break;
      }
      delay(20);
    }
  }
}

#endif // OTHERS_GAMES_ARCADE_H
