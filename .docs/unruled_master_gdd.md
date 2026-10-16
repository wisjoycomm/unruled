# UNRULED — DEEP-DIVE SYSTEM & TECHNICAL SPECIFICATION (MASTER GDD v3.1)

> **Single Source of Truth (AI & Engineering Spec):** Tài liệu đặc tả kỹ thuật và thiết kế hệ thống chi tiết cho dự án **Unruled** (Unity 100% C# Code-First & Dual-Server Architecture). Toàn bộ dữ liệu cân bằng định lượng (20 Items, Recipes, Upkeep, BXH Top 10K) được tham chiếu từ `UNRULED_Master_Data_Sheets_v3.xlsx`.

---

## 1. LUỒNG TRẢI NGHIỆM NGƯỜI CHƠI & VÒNG LẶP CỐT LÕI (PLAYER JOURNEY & CORE LOOPS)

### 1.1. Ba Vòng Lặp Nhịp Độ (3-Tier Real-Time Pacing Loops)
Hệ thống vận hành trên đồng hồ thời gian thực tỷ lệ 1:1 (`1 SimTick = 1 Giây thực tế`, lưu bằng số nguyên 64-bit `long` chuẩn IEEE 754 nhằm triệt tiêu hoàn toàn sai số dấu chấm động):

1. **Vòng lặp Vi mô — Tác chiến & Điều hướng (`Micro Loop`: `1 – 180 Ticks` / Giây đến Phút):**
   - Theo dõi sóng bão vũ trụ `NOAA` (`R/S/G` cấp 1–5) quét qua các cụm sao.
   - Kích hoạt `[QUÉT TẬP TRUNG 60s]` (tiêu tốn ngay `2x ITEM_BATTERY`) vào các tuyến Vành Đai nghi có địch phục kích, hoặc bấm `[NHỔ NEO RÚT LUI]` (chịu `180 Ticks` đếm ngược rút mũi khoan) cho tàu khai thác trước khi bão `R3+` khóa sóng liên lạc.
   - Trong giao tranh loạt đạn (`Salvo Combat`): Can thiệp gạt thanh cường độ bắn (`ROF Stance`: `50% / 100% / 150%`), chuyển mục tiêu ưu tiên (`LineShips / EwShips / SupplyTenders`), mở van xả `ITEM_COOLANT` khi nhiệt độ thân tàu chạm ngưỡng `85°C` (tránh nổ kho đạn ở `90°C`), và phóng tàu tiếp tế nóng (`Hot-Drop Resupply`) vào giữa trận.

2. **Vòng lặp Trung mô — Hậu cần & Công nghiệp (`Meso Loop`: `1,800 – 14,400 Ticks` / 30 Phút đến 4 Giờ):**
   - Cân đối tỷ lệ khai thác 10 loại nguyên liệu thô (`RAW_*`) và vận hành dây chuyền nhà máy chế tạo 10 mặt hàng thành phẩm (`ITEM_*`) theo mô hình Input-Output Wassily Leontief.
   - Thiết lập các đoàn tàu vận tải (`CargoConvoys`) cấp `ITEM_OXYGEN`, `ITEM_RATIONS`, `ITEM_FUEL`, `ITEM_COOLANT` nuôi các mỏ xa và hạm đội tiền tuyến (nếu đứt `RAW_WATER` hoặc `ITEM_OXYGEN` quá `30 Ticks`, hành tinh bị `WHO` phong tỏa kiểm dịch `300 Ticks`).
   - Đưa tàu thu thập dữ liệu bão thực địa (`TRL 1–3`), đốt vật tư chạy thử nghiệm nguyên mẫu tại xưởng (`TRL 4–6`) và gọi hạm đội đời cũ về cải hoán (`Retrofit TRL 7–9`).

3. **Vòng lặp Vĩ mô — Địa chính trị & Bành trướng (`Macro Loop`: `86,400 – 604,800 Ticks` / Ngày đến Tuần):**
   - Chuyển dịch từ khai thác mỏ bề mặt (`Surface Vein`) sang khoan lõi sâu (`Deep Crust`) khi tài nguyên cạn kiệt.
   - Chiếm đóng `Core Base` mới, thực hiện quy trình **Chuyển Base Chính 3 Pha** (`60–120 phút`) để dời tâm tính Độ trễ lệnh (`15s Origin`) ra tiền tuyến.
   - Thao túng thanh khoản sàn `WTO`, nộp hồ sơ kiểm toán định kỳ đua **BXH Top 10,000** để hưởng chiết khấu cọc `Escrow` (từ `30%` xuống `15%`), hoặc liên minh đánh sập `System Citadel` lập Hội đồng Quản trị (`The Directorate`).

### 1.2. Tiến Trình 4 Giai Đoạn Của Một Đế Chế (4-Stage Progression)
- **Giai đoạn 1 — Khai Hoang Trong Vỏ Bọc (`Tech Tier 1` | Giờ 1 – Giờ 12):**
  - Người chơi khởi đầu tại Hệ sao riêng (`Home Star System`) sinh từ `ulong SystemSeed`.
  - Nhờ lớp từ trường Nhật quyển (`Heliopause Cloaking`) và Cổng nhảy liên sao đang tắt (`Dormant`), hệ sao hoàn toàn vô hình và bất khả xâm phạm.
  - Xây dựng chuỗi `RAW_IRON -> ITEM_STEEL`, `RAW_WATER + RAW_NITROGEN -> ITEM_OXYGEN`, đóng các tàu `Tier 1` đầu tiên.
- **Giai đoạn 2 — Mở Cổng & Xung Đột Vành Đai (`Tech Tier 2–3` | Ngày 1 – Ngày 7):**
  - Hệ sao đầu không có `RAW_TUNGSTEN` và `RAW_URANIUM`; mỏ bề mặt cạn dần. Người chơi nâng lên `Tech Tier 2`, nạp năng lượng mở Cổng nhảy nối vào **Cụm Sao Chiến Trường Chung (`Contested Star Cluster`)** — chấm dứt bảo hộ vĩnh viễn.
  - Sử dụng đường vòng Vành Đai (`Outer_Rim_Asteroid_Bypass`) để ẩn `90%` tín hiệu đi đánh lén mỏ lẻ; dựng chuỗi tàu `EwShips` làm trạm tiếp sóng (`Relay`) giảm độ trễ lệnh từ `35s/Hop` xuống `8s/Hop`.
- **Giai đoạn 3 — Chiến Tranh Công Nghiệp & Dời Đô (`Tech Tier 4` | Tuần 2 – Tuần 3):**
  - Mở khóa Liên lạc Laser xuyên bão `R-Scale` và Động cơ đi đường tắt xuyên tâm bão `G4/S4`.
  - Tổ chức các chiến dịch **Hợp kích Đa Tuyến (`Multi-Lane Flanking`)** đánh chiếm hành tinh trung tâm và thực hiện **Chuyển Base Chính 3 Pha**.
- **Giai đoạn 4 — Hạt Nhân Chiến Lược & Lật Đổ Hệ Thống (`Tech Tier 5` | Tuần 4+):**
  - Đào `RAW_URANIUM`, đúc `ITEM_NUCLEAR_CORE` và đóng soái hạm `Tier 5` (chấp nhận bị `IAEA` phát sóng vị trí 24/7 trên toàn bản đồ).
  - Bắn hạt nhân làm nứt vỏ hành tinh lộ vỉa quặng lõi sâu hoặc tổng tấn công `System Citadel`.

---

## 2. ĐẶC TẢ KỸ THUẬT & TOÁN HỌC TỪNG MODULE (CORE ENGINE MECHANICS)

### 2.1. Module Sinh Vũ Trụ & Cạn Kiệt Tài Nguyên (`Unruled.Core.Universe`)

#### A. Thuật toán Sinh Hệ Sao Tất Định (`Deterministic System Generator`)
Từ một giá trị `ulong SystemSeed` 64-bit duy nhất, hàm băm `SplitMix64` kết hợp `ChaCha8` sinh ra cấu trúc đồ thị phẳng:
- **Tọa độ Điểm neo ($S_i$):** Phân bố theo tọa độ cực $(r_i, \phi_i)$ quanh sao chủ nhằm đảm bảo khoảng cách tối thiểu giữa 2 hành tinh bất kỳ $\Delta d >= 120$ units:
  `r_i = R_min + i * Delta_R + Noise(seed, i)`
  `phi_i = (phi_{i-1} + 2*PI / GoldenRatio + Jitter(seed, i)) mod 2*PI`
- **Chỉ số Tương đồng Trái Đất (`ESI`) theo Khoảng cách Vùng Ở Được (`Habitable Zone` — $R_{HZ}$):**
  `ESI_0(r_i) = Clamp01(1.0 - Abs((r_i - R_HZ) / W_HZ)^2 + AtmoNoise)`
  - Hành tinh có `ESI_0` thuộc `[0.70, 1.00]` giàu `RAW_BIOMASS`, `RAW_WATER`, `RAW_NITROGEN`.
  - Hành tinh khắc nghiệt có `ESI_0` thuộc `[0.00, 0.35]` tập trung `RAW_TITANIUM`, `RAW_TUNGSTEN`, `RAW_URANIUM`.

#### B. Phương trình Khai khoáng, Suy thoái Mỏ & Trạng thái Hạt nhân
Mỗi mỏ tài nguyên trên hành tinh gồm 3 bể trữ lượng tách biệt: `SurfaceReserve` ($R_{surf}$), `DeepCrustReserve` ($R_{deep}$), và `CoreFractureReserve` ($R_{core}$ — mặc định `IsUnlocked = false`).
- **Sản lượng khai thác mỗi Tick (`Delta_Q_mine`):**
  `Delta_Q_mine = N_rigs * BaseRate * DepthMult * EsiMult * StormMult`
  - **Trạng thái 1 (`PRISTINE_WORLD` — $R_{surf} > 0$):** `DepthMult = 1.0`, tiêu hao `1.0x ITEM_FUEL` và `1.0x ITEM_COOLANT`.
  - **Trạng thái 2 (`DEEP_CORE_STRIPPING` — $R_{surf} == 0, R_{deep} > 0$):** `DepthMult = 0.5`, tiêu hao `2.0x ITEM_FUEL` và `2.0x ITEM_COOLANT`, chỉ số `CurrentEsi` giảm `-0.05`. Khi bấm rút lui phải chờ `180 Ticks` (3 phút) thu hồi mũi khoan.
  - **Trạng thái 3A (`DEPLETED_BARREN_HUSK` — $R_{surf} == 0, R_{deep} == 0$):** `Delta_Q_mine = 0` vĩnh viễn, `CurrentEsi` giảm `-0.20`. Không xóa Node khỏi đồ thị; giữ nguyên làm Kho trung chuyển, Trạm Tiếp Sóng `Relay` hoặc Pháo đài.
  - **Trạng thái 3B (`IRRADIATED_DEAD_WORLD` — Trúng `ITEM_NUCLEAR_CORE`):**
    - Xóa sổ `100%` `RAW_BIOMASS`; đưa `CurrentEsi = 0.00`.
    - Phát bức xạ nền tương đương **Bão `S4`** trong `86,400 – 259,200 Ticks` (`24h – 72h`).
    - Mở khóa bể thứ 3 (`IsCoreFractureUnlocked = true`): Lộ vỉa quặng lõi (`RAW_TUNGSTEN`, `RAW_TITANIUM`, `RAW_URANIUM`) với `DepthMult = 1.0`, chi phí `4.0x ITEM_FUEL` / `4.0x ITEM_COOLANT` (chỉ tàu `Tech Tier 4–5` mới chịu được bức xạ `S4` để vào đào).
  - **Trạng thái 4 (`TERRAFORMED_RECOVERY` — Cuối game `Tech Tier 5`):** Bơm `RAW_WATER + RAW_NITROGEN + RAW_BIOMASS + ITEM_NUCLEAR_CORE` để khử bức xạ `S4` và kéo `CurrentEsi` từ `0.00` lên `0.60`.

---

### 2.2. Module Kinh Tế Học Chuỗi Cung Ứng & Thị Trường WTO (`Unruled.Core.Economy`)

#### A. Máy Trạng Thái Nhà Máy Tinh Luyện (`Refinery State Machine`)
Mỗi công trình chế tạo trên hành tinh chạy chu kỳ kiểm tra nguyên liệu mỗi `10 Ticks`:
1. **`IDLE_STARVED` (Thiếu nguyên liệu):** Nếu `Stockpile[Input_1] < Qty_1` hoặc `Stockpile[Input_2] < Qty_2`, nhà máy ngừng chạy, tốn `20%` bảo trì nền.
2. **`ACTIVE_PRODUCTION` (Đang vận hành):** Trừ nguyên liệu đầu vào theo công thức Leontief, cộng thành phẩm vào kho hành tinh.
3. **`GRID_BROWNOUT` (Sập nguồn do Bão Địa Từ `G-Scale`):** Khi chịu bão `G2–G5`, công suất tụt về `0%` trừ khi tự động đốt `ITEM_BATTERY` dự phòng.
4. **`WHO_QUARANTINED` (Phong tỏa Kiểm dịch Y tế):** Nếu tồn kho `RAW_WATER` hoặc `ITEM_OXYGEN` bằng `0` liên tục quá `30 Ticks`, cơ quan `WHO` khóa toàn bộ lệnh phóng tàu xuất khẩu rời khỏi hành tinh trong `300 Ticks` (5 phút).

#### B. Thuật toán Định giá `VWAP` & Khớp lệnh Đặt cọc `WTO Escrow`
- **Giá Tham chiếu Trung bình Gia quyền Khối lượng (`VWAP` 100 Ticks):**
  `P_VWAP(t) = Sum(P_trade * V_trade) / Sum(V_trade)` (trong 100 Ticks gần nhất).
- **Biên độ Giá Trần / Sàn Chính ngạch (`WTO Price Band`):**
  `P_min(t) = 0.80 * P_VWAP(t)` và `P_max(t) = 1.20 * P_VWAP(t)`.
- **Quy trình Khóa Cọc & Bồi thường Hợp đồng (`WTO Escrow Workflow`):**
  1. Bên Mua thanh toán `100% * (Q * P)` vào quỹ tạm giữ `WTO`; Bên Bán khóa tiền cọc `Escrow_locked = EscrowRate(Rank) * (Q * P)` (mặc định `30%`, giảm xuống `15%–28%` nếu thuộc **BXH Top 10,000**).
  2. Phóng tàu `CargoConvoys` chở hàng thực tế trên bản đồ.
  3. Giao thành công: Bên Bán nhận `(1 - Fee_WTO) * (Q * P) + Escrow_locked` (phí sàn `2.0%–5.0%` chảy vào **Quỹ Thuế Thực Thu WTO**). Nếu tàu bị cướp/bão đánh chìm: Bên Mua được đền bù từ tiền cọc; hàng bị cướp gắn cờ `Stolen_WTO_Cargo` không thể bán lại lên sàn `WTO`.

---

### 2.3. Module Nghiên Cứu Công Nghệ Chuẩn `NASA TRL` (`Unruled.Core.Research`)
Mỗi cấp công nghệ (`5 Tier x 4 Nhánh x 3 Giai đoạn = 60 Mốc TRL`) đòi hỏi 3 bước thực thi vật lý:
1. **`TRL 1–3` (`Field Telemetry`):** Điều tàu gắn `ITEM_MICROCHIP` bay trực tiếp vào tâm bão `NOAA` thực tế để thu thập đủ `100%` dữ liệu đo đạc.
2. **`TRL 4–6` (`Prototype Stress Test`):** Đốt vật tư chạy thử nghiệm nguyên mẫu tại xưởng. Xác suất thành công ở lần thử thứ $k$ ($k >= 1$) có bảo hiểm cộng dồn (`Calibration Pity`):
   `P_success(k) = Min(1.0, P_base(Tier) + (k - 1) * 0.18)`
   *(Với `P_base`: Tier 2 = `60%`, Tier 3 = `45%`, Tier 4 = `30%`, Tier 5 = `20%`. Mỗi lần nổ cộng `+18%` cho lần sau nhưng phát xung chấn động báo lên bản tin `NOAA/IAEA` trong bán kính 3 Hops).*
3. **`TRL 7–9` (`Factory Retooling & Ship Retrofit`):** Nâng cấp xưởng đúc và gọi các Quân cờ đời cũ từ tiền tuyến quay về xưởng để cải hoán lên đời mới (`Retrofit`).

---

### 2.4. Module Quân Sự, Độ Trễ Tín Hiệu, Radar 60s & Giao Chiến Salvo (`Unruled.Core.Military`)

#### A. Đồ thị Độ Trễ Truyền Lệnh (`Signal Latency`) & Chuyển Đô 3 Pha
Mọi mệnh lệnh điều khiển hạm đội được gắn nhãn `ExecuteAtTick = CurrentTick + SignalLatency`:
- **Công thức Tìm Đường Truyền Tín Hiệu Ngắn Nhất (`Dijkstra Relay Solver`):**
  Gọi $d$ là số bước nhảy (`Hops`) từ Cụm sao chứa `Core Base` hiện tại ($d = 0$):
  - Bước nhảy thứ $i$ CÓ đặt **Trạm Tiếp Sóng (`Relay Station`)** hoặc **Tàu `EwShips`**: trọng số $w_i = 8	ext{ Ticks}$.
  - Bước nhảy thứ $i$ KHÔNG có tiếp sóng: trọng số $w_i = 35	ext{ Ticks}$.
  - Tổng độ trễ:
    `SignalLatency = (15 + Sum_{i=1..d}(w_i)) * RelocationMult`
    *(Trong đó `RelocationMult = 1.0` bình thường, và `RelocationMult = 2.0` khi đang ở **Pha 2: Đoàn Tàu Chuyển Đô đang bay trên đường**).*

#### B. Giao thức Quét Radar Tập Trung 60 Giây & Đối kháng Điện tử (`IEEE Std 686`)
1. **`Tick 0` (Kích hoạt Quét Vành Đai):** Trừ lập tức **`2x ITEM_BATTERY`** (`Startup Surge Cost` — chống trò bật/tắt Radar nháy liên tục).
2. **`Tick 15` (Cảnh báo sớm `RWR` cho phe ẩn nấp):** Nếu hạm đội đang luồn Vành Đai có tối thiểu `1 EwShip`, còi `RWR` báo động trước **45 giây** để chọn: bấm `[REVERSE / QUAY ĐẦU]` rút êm hoặc bấm `[ECM JAMMING]` đốt `2x ITEM_BATTERY` làm tăng thời gian quét của địch từ **`60 Ticks` lên `120 Ticks`**.
3. **`Tick 60` (Hoặc `Tick 120` nếu bị Jamming):** Duy trì đủ thời gian chiếu xạ liên tục mới hiện rõ quân địch trong Vành Đai; tắt sớm mất trắng Pin và hủy kết quả.

#### C. Phương trình Toán học `SalvoCombatEngine` & Hợp Kích Đa Tuyến (`Multi-Lane Flanking`)
Giao chiến diễn ra theo từng chu kỳ Loạt đạn (`1 Salvo Cycle = 5 Ticks = 5 Giây thực tế`):
1. **Hệ số Bao vây Đa Tuyến (`Flanking Suppression Factor` — $F_{flank}$):**
   - Hai hướng tiến công $u, v$ vào hành tinh được tính là **Tuyến Hợp Kích Độc Lập** nếu góc phương vị kẹp giữa chúng $\Delta	heta_{u,v} >= 90^\circ$.
   - Với $L_{extra} \in \{0, 1, 2, 3\}$ là số tuyến hợp kích mở thêm ngoài hướng chính, hiệu quả lưới đánh chặn `Point-Defense` của Phe Thủ giảm theo công thức:
     `F_flank = Max(0.20, 1.0 - 0.20 * L_extra)`
2. **Phương trình Sát thương Loạt đạn (`Hughes Salvo Equation`):**
   - Xung lượng hỏa lực Phe A: `Psi_A = Sum(LineShips_A * Alpha * ROF_Mult)` (khi `Ammo_A > 0`).
   - Năng lực đánh chặn Phe B: `Omega_B = (Sum(EwShips_B * Beta) + FortificationBonus) * F_flank` (khi `Battery_B > 0`).
   - Sát thương xuyên giáp vào lớp tàu mục tiêu (`FocusTarget`): `Delta_HP_B = Max(0, Psi_A - Omega_B)`.
3. **Phương trình Nhiệt động lực học Thân tàu (`Thermal Runaway`):**
   - Nhiệt độ thân tàu tăng sau mỗi loạt bắn: `T_hull(t+5) = T_hull(t) + dT_fire * (ROF_Mult)^2 - dT_passive - dT_coolant`.
   - Vượt quá **`90°C`**: Khóa pháo về `0% ROF` và gây nổ kho đạn nội bộ trừ `5% HP` mỗi loạt cho đến khi xả `ITEM_COOLANT` hạ nhiệt xuống dưới `75°C`.

---

### 2.5. Bộ Não `UtilityAiEngine` Dùng Chung Cho Player Auto-SOP & Bot PvE (`Unruled.Core.AI`)
Chạy trên **`Server 2 (Headless AI Worker)`** mỗi `10 Ticks` cho cả người chơi đang Offline (`Standing Orders`) và Bot PvE theo công thức trung bình nhân có bù trừ (*Dave Mark IAUS*):
`Score(action) = PresetWeight(action) * (Product_{i=1..m}(Curve_i(x_i)))^(1/m)`
- Tự động kích hoạt lệnh rút mũi khoan (`180 Ticks`) khi bão `NOAA` vượt ngưỡng `Evacuate_Storm_Level` hoặc khi lực lượng địch vượt tỷ lệ `Retreat_Force_Ratio`.
- Tự động xả `ITEM_COOLANT` khi nhiệt độ chạm ngưỡng cài đặt (`70°C–88°C`) và tự động chia quân hợp kích đa tuyến ($\Delta	heta >= 90^\circ$).

---

## 3. ĐẶC TẢ CẤU TRÚC DỮ LIỆU C# CỐT LÕI (`Unruled.Core` DATA SCHEMAS)

```csharp
using System;
using System.Collections.Generic;

namespace Unruled.Core
{
    // 1. ĐỒNG HỒ THỜI GIAN THỰC 1:1 (1 TICK = 1 GIÂY)
    public readonly struct SimTick : IComparable<SimTick>, IEquatable<SimTick>
    {
        public readonly long Value; // Số nguyên 64-bit, triệt tiêu hoàn toàn float drift
        public SimTick(long value) => Value = value;
        public long ToMinutes() => Value / 60L;
        public long ToHours() => Value / 3600L;
        public long ToDays() => Value / 86400L;
        public int CompareTo(SimTick other) => Value.CompareTo(other.Value);
        public bool Equals(SimTick other) => Value == other.Value;
    }

    // 2. TRẠNG THÁI VÒNG ĐỜI HÀNH TINH & MỎ 3 TẦNG
    public enum PlanetLifecycleState : byte
    {
        PristineWorld = 1,
        DeepCoreStripping = 2,
        DepletedBarrenHusk = 3,
        IrradiatedDeadWorld = 4,
        TerraformedRecovery = 5
    }

    public struct MineralDeposit
    {
        public byte RawItemId;              // 1..10 (RAW_WATER -> RAW_URANIUM)
        public int SurfaceReserve;          // Tầng 1: Đào 100% tốc độ, 1x Fuel/Coolant
        public int DeepCrustReserve;        // Tầng 2: Đào 50% tốc độ, 2x Fuel/Coolant, -0.05 ESI
        public int CoreFractureReserve;     // Tầng 3: Mở khóa khi trúng Hạt nhân, 4x Fuel/Coolant
        public bool IsCoreFractureUnlocked;
    }

    public sealed class SiteNode
    {
        public int SiteId;
        public int ClusterId;
        public ulong TerrainSeed;
        public float PosX, PosY;
        public int OwnerPlayerId;           // 0 = Neutral, -1..-5 = THE SYSTEM, >0 = Player/Bot
        public bool IsCoreBase;
        public float BaseEsi;               // 0.00f -> 1.00f
        public float CurrentEsi;
        public PlanetLifecycleState LifecycleState;
        public long IrradiatedUntilTick;    // Phát xạ S4 nếu bị bắn ITEM_NUCLEAR_CORE
        public long WhoQuarantineUntilTick; // Khóa xuất khẩu 300 Ticks nếu đứt Oxy > 30 Ticks
        public bool HasActiveRelayStation;  // Giảm độ trễ truyền lệnh từ 35s xuống 8s/Hop
        public int[] Stockpile = new int[21]; // Index 1..20 tương ứng 20 Item
        public List<MineralDeposit> Deposits = new List<MineralDeposit>();
    }

    // 3. QUÂN CỜ 4 LỚP TÀU, NHIỆT ĐỘ SALVO & CHẾ ĐỘ NIÊM CẤT (MOTHBALL)
    public enum TargetFocusClass : byte { LineShips = 0, EwShips = 1, SupplyTenders = 2, CargoConvoys = 3 }
    public enum RofStance : byte { Conservation50 = 0, Standard100 = 1, Overdrive150 = 2 }

    public sealed class FleetUnit
    {
        public int FleetId;
        public int OwnerPlayerId;
        public byte TechTier;               // 1..5 (Tier 5 bị IAEA phát sóng toàn bản đồ)
        public int LineShipCount;
        public int EwShipCount;
        public int SupplyTenderCount;
        public int CargoConvoyCount;

        // Trạng thái vị trí & Di chuyển trên Starlane
        public int CurrentSiteId;           // -1 nếu đang bay trên Starlane
        public int TransitEdgeId;
        public float TransitProgress;       // 0.0f -> 1.0f
        public bool IsHoldingInLane;        // Đang dừng chốt chặn giữa đường
        public float ApproachAzimuthDeg;    // Góc phương vị 0..360 độ phục vụ Hợp kích Đa Tuyến

        // Trạng thái Hậu cần, Nhiệt học & Niêm cất
        public float HullIntegrityPct;      // 0.0f -> 100.0f
        public float HullTemperatureC;      // Bình thường 20°C, >90°C nổ kho đạn
        public bool IsMothballed;           // Giảm 80% Upkeep
        public long MothballWarmupUntilTick;// Chờ 60..180 Ticks khi bật lại lò
        public long DrillRetractUntilTick;  // Chờ 180 Ticks (3 phút) khi rút mũi khoan tháo chạy
        public TargetFocusClass FocusTarget;
        public RofStance FireRateStance;
    }

    // 4. HÀNG ĐỢI LỆNH CÓ ĐỘ TRỄ TÍN HIỆU (DÙNG CHUNG CHO PLAYER & SERVER 2 AI)
    public struct DelayedPlayerCommand
    {
        public long IssuedAtTick;
        public long ExecuteAtTick;          // = IssuedAtTick + SignalLatencySolver.Compute(...)
        public int PlayerId;
        public byte CommandType;            // Move, Hold, Reverse, ScanBelt, SetROF, RelocateBase...
        public int TargetEntityId;
        public int ParamA;
        public int ParamB;
    }
}
```

---

## 4. QUY TẮC XỬ LÝ NGOẠI LỆ & CHỐNG TRỤC LỢI (EDGE CASES & ANTI-EXPLOIT RULES)

1. **Chống Trục Lợi "Nháy Radar" (`Radar Strobe Exploit`):**
   - Trừ thẳng **`2x ITEM_BATTERY`** ngay tại `Tick 0` khi bấm nút (`Startup Surge Cost`) và không trả về bất kỳ dữ liệu nào cho đến khi duy trì đủ **`60 Ticks` liên tục**.
2. **Chống Trục Lợi "Dời Đô Trốn Chết" (`Combat Capital Hopping`):**
   - Nút `[CHUYỂN BASE CHÍNH]` bị khóa cứng (`Disabled`) ngay khi có bất kỳ Quân cờ thù địch nào nằm trên quỹ đạo `Core Base` hoặc đang bay trên các tuyến `Starlanes` nối trực tiếp vào `Core Base` đó.
3. **Chống Trục Lợi "Rửa Tiền Bảo Hiểm WTO" (`Escrow Self-Piracy Exploit`):**
   - Tiền đền bù bảo hiểm cho Bên Mua được trích trực tiếp từ khoản **`Escrow 30%`** của Bên Bán; đồng thời hàng hóa bị cướp từ hợp đồng `WTO` bị gắn nhãn `Stolen_WTO_Cargo` — không thể đem bán lại trên sàn chính ngạch `WTO`.
4. **Chống Lạm Phát Từ Phần Thưởng Bảng Xếp Hạng (`Zero-Inflation Tax Pool`):**
   - Server tuyệt đối không in thêm tiền/vật tư từ hư không để làm phần thưởng BXH; toàn bộ cổ tức `Credits` và gói trợ cấp `ITEM_FUEL / ITEM_STEEL / ITEM_COOLANT` chia cho 6 bậc trên BXH đều được trích `100%` từ **Quỹ Thuế Thực Thu (`WTO Market Fee Pool` & `STA Toll Gate Pool`)**.

---

## 5. LỘ TRÌNH XÂY DỰNG KỸ THUẬT 5 SPRINT (TECHNICAL IMPLEMENTATION ROADMAP)

- **Sprint 1 — Lõi Mô Phỏng Tất Định & Chuỗi Cung Ứng (`Unruled.Core.Simulation` & `Economy`):**
  - Thiết lập `Unruled.Core.asmdef` (`noEngineReferences: true`), `SimTick`, `SystemSeedGenerator`, kho hàng 20 Item, công thức `RecipeDatabase` và bộ kiểm thử `NUnit Edit-Mode Tests`.
- **Sprint 2 — Đồ Thị Đường Bay 3 Tầng, Bão NOAA & Độ Trễ Tín Hiệu (`Universe` & `Latency`):**
  - Lập trình đồ thị `SiteNode` (5 trạng thái vòng đời, 3 tầng mỏ), `StarlaneEdge` (Cao tốc STA, Vành Đai, Xuyên Bão), `NoaaWeatherEngine` (`R/S/G 1–5`), `SignalLatencySolver` (`15 + 35d` vs `15 + 8d`) và `FocusedRadarSystem` (`60s` + cảnh báo `RWR 45s`).
- **Sprint 3 — Giao Chiến Hughes Salvo, La Bàn Hợp Kích 360° & Chuyển Đô 3 Pha (`Military`):**
  - Cài đặt `SalvoCombatEngine` (tích nhiệt `90°C`, đánh kẹp đa tuyến giảm `20% PD/tuyến`, tiếp tế nóng giữa trận), chế độ `Mothball` (`-80% Upkeep`), độ trễ rút mũi khoan `180 Ticks` và `CapitalRelocationSystem`.
- **Sprint 4 — Định Chế `THE SYSTEM`, Sàn `WTO Escrow`, BXH Top 10K & `UtilityAiEngine` (`AI` & `SystemFactions`):**
  - Hoàn thiện 5 cơ quan (`STA, WTO, WHO, IAEA, NOAA`), cơ chế `Call-to-Arms` 2 lớp ngưỡng, kiểm toán định kỳ BXH Top 10,000 và bộ chấm điểm `UtilityAiEngine` chạy tách biệt trên `Server 2 Worker`.
- **Sprint 5 — Sa Bàn Chiến Thuật 1 Tầng & Giao Diện Chỉ Huy 4 Vùng (`Unruled.View`):**
  - Dựng `GameBootstrapper.cs`, vẽ đồ thị không gian bằng `LineRenderer`/`Mesh` và kết nối toàn bộ dữ liệu vào giao diện **Unity UI Toolkit (`UnruledCommandDeck.uxml` / `.uss`)** với nền đặc 100%, chữ sắc nét, không Popup.
