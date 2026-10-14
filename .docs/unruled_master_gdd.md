# UNRULED — TÀI LIỆU THIẾT KẾ HỆ THỐNG & KỸ THUẬT TỔNG HỢP (MASTER GDD)

> **Dành cho AI Agent & Developer:** Tài liệu này là nguồn chân lý duy nhất (Single Source of Truth) đã được khóa (Locked Specification) của dự án **Unruled**. Toàn bộ mã nguồn sử dụng **Unity Engine (100% C#)** theo kiến trúc tách biệt hoàn toàn giữa `Unruled.Core` (Pure C# - không phụ thuộc `UnityEngine`) và `Unruled.View` (Unity Presentation Layer & UI Toolkit).

---

## 1. Tầm nhìn, Nền tảng & Kiến trúc Kỹ thuật

### 1.1. Định hướng Cốt lõi (Vision)
- **Thể loại:** Chiến thuật quản lý tài nguyên vĩ mô, chuỗi cung ứng công nghiệp và xâm chiếm hành tinh dựa trên các quy luật kinh tế học (Input-Output Leontief, Chi phí vận tải Samuelson) và hậu cần quân sự thực tế (NASA ISRU, USGS, NATO Supply Classes).
- **Vai trò người chơi:** Nhà quản lý vĩ mô (không có nhân vật đại diện cá nhân, không điều khiển lính lẻ tẻ).
- **Chế độ chơi & Ngoại giao:** Multiplayer tự do (Người chơi với Người chơi + Thế lực Định chế Trung lập `THE SYSTEM`). **Không có hệ thống liên minh hay ngoại giao lập trình sẵn** — người chơi tự đàm phán, hợp tác, giao thương hoặc phản bội nhau thông qua các công cụ vật lý (mở/đóng tuyến vận tải, ký/hủy hợp đồng, cắt nguồn tiếp tế).
- **Nhịp độ thời gian (Real-Time 1:1):** Thời gian thực tỷ lệ chuẩn `1 Tick = 1 Giây` (`60 Ticks = 1 Phút`, `3,600 Ticks = 1 Giờ`, `86,400 Ticks = 1 Ngày`).

### 1.2. Kiến trúc Mã nguồn Unity (100% C# Code-First)
- **Đa nền tảng:** WebGL, Desktop (Windows/macOS/Linux), Mobile.
- **Phân tách Assembly (`.asmdef`):**
  - `Unruled.Core.asmdef`: 100% Pure C# (`No Engine References: true`). Chứa toàn bộ mô phỏng đồ thị bản đồ, chuỗi 20 Item, thị trường WTO, bão vũ trụ NOAA, hậu cần quân sự và kiểm thử tự động qua **NUnit Edit-Mode Tests**.
  - `Unruled.View.asmdef`: Hiển thị bàn cờ thiên văn bằng `LineRenderer`/Mesh và giao diện **Unity UI Toolkit (`.uxml` / `.uss`)** với nền đặc 100% (không làm mờ), chữ tương phản cao sắc nét.

---

## 2. Phần 1: Bản đồ, Không gian, Điểm neo & Thời tiết Vũ trụ

### 2.1. Bản đồ 1 Tầng & Hệ thống Tuyến đường (`Starlanes`)
- **Bàn cờ 1 Tầng duy nhất:** Toàn bộ hoạt động kinh tế, di chuyển và giao tranh diễn ra trên một mặt phẳng sa bàn thiên văn duy nhất.
- **Di chuyển cố định trên `Starlanes`:** Tàu vận tải (`Convoys`) và Quân cờ (`Fleets`) bắt buộc di chuyển dọc theo các tuyến đường nối sẵn giữa Điểm A và Điểm B (không bay tự do ngoài không gian trống), tạo ra các nút thắt cổ chai (`Chokepoints`) chiến lược.
- **3 Phân loại Tuyến đường:**
  1. **Tuyến Cao tốc `STA` (`Toll Highway`):** Ngắn nhất, tốn ít `ITEM_FUEL` nhất, an toàn khỏi bão (`G0/S0`), nhưng phải nộp phí qua trạm cho cơ quan `STA`.
  2. **Tuyến Đường Vòng Xa (`Outer Rim Bypass`):** Miễn phí qua trạm (`0 Credits`), nhưng quãng đường xa gấp 4–5 lần (tốn `4x–5x ITEM_FUEL`) và chịu bão nhẹ (`G1–G2`). Dùng để né lệnh cấm vận hoặc đi vòng đánh úp.
  3. **Tuyến Đường Tắt Qua Tâm Bão (`Hazard Shortcut`):** Miễn phí, đường ngắn (`1.5x ITEM_FUEL`), nhưng đi xuyên qua vùng bão khắc nghiệt (`G4/S4/R4`). Dành cho tàu công nghệ cao (`Tech Tier 4–5`) hoặc vận chuyển hàng lậu (`RAW_URANIUM`) né mắt thanh sát `IAEA`.

### 2.2. Đặc tính Hành tinh (`Sites`) & Chỉ số Sự sống (`ESI`)
- **Hành tinh khởi đầu (`Spawn Worlds`):** Có chỉ số cân bằng tuyệt đối giữa mọi người chơi (`ESI = 0.85 - 1.00`, an toàn khỏi bão).
- **Chỉ số Tương đồng Trái Đất (`ESI` từ `0.00` đến `1.00`):**
  - Đo khả năng duy trì sự sống tự nhiên theo chuẩn PHL/NASA.
  - Các hành tinh có `ESI < 0.50` không thể định cư lâu dài; người chơi chỉ có thể điều tàu tới cắm neo khai thác tạm thời rồi phải rút lui (`Evacuate`) trước khi bão lớn quét qua.
- **Độ trễ Rút mũi khoan (`Emergency Disconnect Sequence - EDS`):** Tàu đang cắm neo khai thác muốn rút lui phải mất `180 Ticks` (`3 phút thực tế`) để thu hồi giàn khoan trước khi có thể di chuyển.
- **Chuyên môn hóa Hành tinh (`Specialization Doctrine`):** Mỗi hành tinh có lợi thế tự nhiên riêng. Khi chiếm đóng, người chơi phải chọn 1 trong 4 hướng quy hoạch (**Khai khoáng / Công nghiệp / Nghiên cứu / Quân sự**); chọn lệch lợi thế tự nhiên sẽ bị phạt giảm hiệu suất đầu ra.

### 2.3. Hệ thống Bão Vũ trụ theo Thang đo NOAA (Cấp 1 $\rightarrow$ 5)
1. **`R-Scale` (`R1–R5` Radio Blackout):** Tia X đi với vận tốc ánh sáng tới trước — gây mù radar và **khóa quyền gửi lệnh điều khiển / khóa nút rút lui (`Evacuate`)** đối với các đơn vị nằm trong vùng tâm bão.
2. **`S-Scale` (`S1–S5` Solar Radiation Storm):** Đám mây bức xạ hạt tới sau — gây hao hụt nhân lực và độ bền của đoàn khai thác/hạm đội nếu cấp độ Công nghệ (`Tech Tier`) của người chơi thấp hơn cấp bão `S-Level`.
3. **`G-Scale` (`G1–G5` Geomagnetic Storm):** Bão địa từ — đánh sập lưới điện nhà máy tinh luyện (nếu không có `ITEM_BATTERY`), làm sập khiên chắn và giảm tốc độ bay trên tuyến đường.

---

## 3. Phần 2: Kinh tế, Khai thác & Phát triển Công nghệ

### 3.1. Chuỗi 20 Item Cốt lõi (`10 RAW_*` $\rightarrow$ `10 ITEM_*`)
- Hệ thống khóa cố định **20 mã Item độc lập** (chi tiết thông số tại file Sheet đi kèm):
  - **Cấp 1 (10 Nguyên liệu Thô `RAW_*`):** Khai thác trực tiếp từ mỏ theo phân bố địa chất `ESI` (`RAW_WATER`, `RAW_NITROGEN`, `RAW_METHANE`, `RAW_BIOMASS`, `RAW_IRON`, `RAW_COPPER`, `RAW_SILICON`, `RAW_TITANIUM`, `RAW_TUNGSTEN`, `RAW_URANIUM`).
  - **Cấp 2 (10 Thành phẩm Công nghiệp & Quân sự `ITEM_*`):** Chế biến tại nhà máy theo mô hình Input-Output (`ITEM_OXYGEN`, `ITEM_RATIONS`, `ITEM_COOLANT`, `ITEM_FUEL`, `ITEM_STEEL`, `ITEM_ALLOY`, `ITEM_MICROCHIP`, `ITEM_BATTERY`, `ITEM_AMMO`, `ITEM_NUCLEAR_CORE`).

### 3.2. Suy thoái Mỏ & Độ sâu Khai thác
- Trữ lượng mỏ trên mỗi hành tinh chia làm **3 tầng độ sâu** (Surface / Mantle / Deep Core).
- Càng khai thác sâu, sản lượng gốc càng giảm dần (suy thoái mỏ) và đòi hỏi chi phí bảo trì hạ tầng cao hơn.

### 3.3. Hệ thống Công nghệ Đa điều kiện (NASA TRL 1–9)
- Chia làm **5 Tier $\times$ 4 Nhánh**.
- Để mở khóa một bậc công nghệ, người chơi bắt buộc phải hoàn thành đủ **4 điều kiện thực tiễn (4-Pillar Tech Gate)** thay vì chỉ nộp nguyên liệu:
  1. **Điều kiện Khảo sát Thực địa (Field Survey):** Đã đưa tàu tới thăm dò hoặc cắm neo tại vùng địa chất/bức xạ tương ứng.
  2. **Điều kiện Hạ tầng Công nghiệp (Industrial Capacity):** Đã vận hành nhà máy sản xuất đạt ngưỡng sản lượng thực tế.
  3. **Điều kiện Thử nghiệm Môi trường Khắc nghiệt (Stress Test):** Đã có đơn vị sống sót hoặc vận hành thực tế trong vùng bão `R/S/G` hoặc vùng `ESI` thấp.
  4. **Điều kiện Vật tư Nghiên cứu (Material & Microchip Input):** Cấp đủ `ITEM_MICROCHIP` và vật liệu chuyên dụng cho tổ hợp nghiên cứu.

---

## 4. Phần 3: Quân sự, Hậu cần & Giao chiến (`Autobattle`)

### 4.1. Cấu trúc Quân cờ (`FleetUnit`) & Đường dây Tiếp vận (`SupplyLineSolver`)
- **Tổ hợp 3 Thành phần trong 1 Quân cờ:**
  1. **Tàu Chiến tuyến (`Line Ships`):** Chịu đòn và gây sát thương hỏa lực chính.
  2. **Tàu Tác chiến Điện tử (`EW Vessels`):** Gây nhiễu, đánh chặn tên lửa (`Point-Defense`) và soi radar.
  3. **Tàu Hậu cần (`Supply Tenders`):** Chứa kho đạn, nhiên liệu, chất làm mát cho hạm đội và tích hợp mũi khoan khai thác dã chiến.
- **Đường dây Tiếp vận Vật lý:** Quân cờ ở tiền tuyến bắt buộc phải có tuyến đường tiếp vận thông suốt về kho hậu phương để nhận `ITEM_AMMO`, `ITEM_FUEL`, `ITEM_OXYGEN`, `ITEM_RATIONS` và `ITEM_COOLANT`. Nếu bị đối phương cắt đường tiếp vận, hạm đội sẽ rơi vào trạng thái **cạn đạn (0 sát thương), quá nhiệt (tự nổ trong chân không) hoặc ngạt thở**.

### 4.2. Cơ chế Giao chiến (`Autonomous Salvo with Live Intervention`)
1. **Vận hành tự động (`Autobattle`):** Khi hai Quân cờ chạm trán, hệ thống tự động giải quyết các chu kỳ bắn đạn đạo (`Salvo`), đánh chặn (`Point-Defense`), tính toán tiêu hao theo phương trình Lanchester và tích nhiệt theo từng Tick (`1 giây`). Công thành tại hành tinh diễn ra qua 2 giai đoạn (Quỹ đạo $\rightarrow$ Bề mặt).
2. **Can thiệp Lệnh Chỉ huy giữa trận (`Live Command Override`):**
   - Người chơi có thể đổi **Mục tiêu Ưu tiên** (nhắm vào `Line` / `EW` / `Supply`), chỉnh **Cường độ Hỏa lực** (`50%` / `100%` / `150%`), và đóng/mở **Van xả `ITEM_COOLANT`**.
   - Lệnh can thiệp có độ trễ truyền tín hiệu và **bị khóa hoàn toàn nếu khu vực giao tranh đang chịu bão mất sóng `R3–R5`**.
3. **Tiếp tế Nóng giữa trận (`Hot-Drop Resupply`):**
   - Người chơi có thể điều đoàn tàu vận tải (`Convoys`) chở đạn, chất làm mát và năng lượng lao thẳng vào điểm đang giao chiến để tiếp tế trực tiếp.
   - Nếu đối phương đang đặt lệnh ưu tiên bắn vào `Supply Tenders`, đoàn tàu tiếp vận sẽ bị bắn chặn ngay khi cập mạn.

---

## 5. Phần 4: Định chế Trung lập (`THE SYSTEM`), Thị trường & Chiến thắng

### 5.1. Năm Cơ quan Chấp pháp Trung lập (`THE SYSTEM`)
1. **`STA` (Space Transit Authority):** Quản lý giao thông trên các tuyến `Starlanes` cao tốc, thu phí qua trạm và có quyền khóa cổng giao thông đối với kẻ vi phạm.
2. **`WTO` (World/Galactic Trade Organization):** Quản lý thị trường chính ngạch, tính giá tham chiếu trung bình động (`VWAP/EMA`) mỗi `100 Ticks`, áp biên độ trần/sàn `±20%` và giữ tiền đặt cọc hợp đồng (**Escrow `30%`**) để chống lừa đảo. Tồn tại song song với kênh **Chợ Đen (`Black Market`)** tự do giá cả nhưng không được bảo vệ hợp đồng.
3. **`WHO` (Health/Bio-Safety Organization):** Giám sát chỉ số sinh học. Nếu một hành tinh bị đứt nguồn cung `RAW_WATER` / `ITEM_OXYGEN` quá `30 Ticks`, `WHO` tự động kích hoạt lệnh **Cách ly Kiểm dịch (`Quarantine`)**, khóa mọi hoạt động xuất khẩu của hành tinh đó trong `300 Ticks` (`5 phút`).
4. **`IAEA` (Atomic Energy Agency):** Giám sát hạt nhân. Bất kỳ hành tinh nào khai thác hoặc sử dụng `RAW_URANIUM` / `ITEM_NUCLEAR_CORE` sẽ bị `IAEA` phát sóng công khai vị trí lên toàn bản đồ.
5. **`NOAA` (Space Weather Agency):** Cung cấp dữ liệu dự báo bão vũ trụ. Người chơi có thể trả `Credits` hoặc `ITEM_MICROCHIP` để mua bản tin dự báo sớm độc quyền.

### 5.2. Sương mù Chiến tranh & Radar 3 Mức
- Mặc định thông tin kho hàng và quân số tại hành tinh của đối phương bị che khuất.
- Người chơi phải xây dựng Trạm Radar (tiêu thụ `ITEM_MICROCHIP`) hoặc mua dữ liệu từ `THE SYSTEM` để soi, ngoại trừ các điểm có hoạt động hạt nhân bị `IAEA` chỉ điểm công khai.

### 5.3. Điều kiện Chiến thắng (`VictoryEvaluator`)
- **Con đường 1 — Thống trị Kinh tế (`70% Monopoly`):** Kiểm soát `70%` tổng giá trị thanh khoản và nguồn cung chiến lược trên thị trường `WTO`.
- **Con đường 2 — Trở thành Vô Luật Pháp (`Becoming Unruled` / `Citadel Siege`):**
  - Khi đạt **Tech Tier 5**, tự chủ chuỗi 20 Item và hệ thống Radar riêng, người chơi có thể tuyên chiến và công phá thành lũy trung tâm của `THE SYSTEM` (**Unruled Citadel Siege**).
  - Đi kèm hệ thống 2 lớp ngưỡng kêu gọi lực lượng phòng vệ và chia thưởng bồi hoàn 4 bậc (`30% -> 65% -> 90% -> 100%`) của `THE SYSTEM`, hoặc thiết lập **Hội đồng Quản trị (`The Directorate`)** gồm 6 chức danh (`ChiefAdministrator`, `DirectorOfSTA`, `DirectorOfWTO`, `DirectorOfWHO`, `DirectorOfIAEA`, `DirectorOfNOAA`).

---

## 6. Phần 5: Giao diện Chỉ huy & Cấu trúc Code C# (`Assets/Scripts/`)

### 6.1. Bố cục Giao diện Chỉ huy 4 Vùng Cố định (Unity UI Toolkit)
1. **Thanh Vĩ mô & Cảnh báo NOAA (`TopTelemetryHud` - Cao 48px):** Hiển thị Credits, Credit Rating WTO, Tech Tier, và Đồng hồ đếm ngược Bão NOAA (`R/S/G 1–5`).
2. **Bàn cờ Thiên văn 1 Tầng (`MapBoardRenderer` - Trung tâm 75%):** Hiển thị các `Sites` (kèm nhãn `ESI`), 3 loại đường `Starlanes`, và `FleetUnit` (kèm 3 thanh Ammo, Fuel, Heat/Coolant + nút `[EVACUATE]`).
3. **Bảng Kho Hàng 20 Item & Giá WTO (`LeftInventoryDock` - Rộng 260px):** Chia 2 cột cố định (`10 RAW_*` và `10 ITEM_*`) kèm giá tham chiếu WTO theo thời gian thực.
4. **Bảng Điều khiển Chi tiết & Hợp đồng (`ContractAndMarketUI` - Rộng 320px):** Chọn quy hoạch hành tinh, nâng cấp Tech TRL, ký Hợp đồng WTO (khóa giá `±20%`, cọc Escrow `30%`) hoặc gạt sang Chợ Đen (`Black Market`).

### 6.2. Cấu trúc Thư mục Code C# (`Assets/Scripts/`)
```text
Assets/Scripts/
├── Core/                           <-- Unruled.Core.asmdef (No Engine References: true)
│   ├── Simulation/
│   │   ├── SimClock.cs             // Quản lý nhịp 1 Tick = 1 Giây thực tế & hàng đợi Command
│   │   └── GameBalanceConfig.cs    // Chứa toàn bộ hằng số cân bằng
│   ├── Board/
│   │   ├── SiteNode.cs             // Dữ liệu Hành tinh: ESI, 3 tầng mỏ, quy hoạch
│   │   ├── StarlaneEdge.cs         // Tuyến đường: Khoảng cách, loại đường, phí trạm STA
│   │   └── NoaaWeatherEngine.cs    // Sinh bão G/S/R 1-5; tính hiệu ứng khóa lệnh R-Scale
│   ├── Economy/
│   │   ├── ItemId.cs               // Enum 20 Item độc lập (10 RAW_* + 10 ITEM_*)
│   │   ├── RecipeDatabase.cs       // Công thức Input-Output chuyển từ 10 Thô -> 10 Thành phẩm
│   │   ├── ConvoyRouter.cs         // Tính tiêu hao ITEM_FUEL và điều phối tàu vận tải
│   │   └── WtoMarketEngine.cs      // Tính giá VWAP/EMA mỗi 100 Ticks, trần +-20% và Escrow 30%
│   ├── Military/
│   │   ├── FleetUnit.cs            // Quân cờ 3 thành phần (Line, EW, Supply) + rút khoan 180s
│   │   ├── SupplyLineSolver.cs     // Kiểm tra đường nối hậu cần; trừ Ammo, Fuel, Oxygen, Coolant
│   │   └── LanchesterCombat.cs     // Autobattle Salvo, Live Command Override & Hot-Drop Resupply
│   └── SystemFactions/
│       ├── SystemAuthorityManager.cs // Quản lý 5 cơ quan: STA, WTO, WHO, IAEA, NOAA
│       ├── SystemDefenseEvaluator.cs // Hệ thống ngưỡng phòng vệ & chia thưởng 4 bậc (30-65-90-100%)
│       └── VictoryEvaluator.cs     // Kiểm tra điều kiện thắng (Citadel Siege / 70% Monopoly)
│
└── View/                           <-- Unruled.View.asmdef (MonoBehaviour & UI Toolkit)
    ├── MapBoardRenderer.cs         // Vẽ bản đồ 1 tầng, đường Starlanes và ký hiệu Quân cờ
    └── UI/
        ├── TopTelemetryHud.cs      // Thanh cảnh báo bão NOAA và chỉ số vĩ mô
        └── ContractAndMarketUI.cs  // Giao diện ký hợp đồng WTO vs Chợ Đen
```