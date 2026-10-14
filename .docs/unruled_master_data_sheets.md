# UNRULED — BẢNG DỮ LIỆU CHỈ SỐ HỆ THỐNG (MASTER DATA SHEETS)

> **Hướng dẫn đưa vào Google Sheets / Excel:** Mỗi bảng dưới đây tương ứng với 1 Tab (Sheet) dữ liệu. Bạn có thể bôi đen trực tiếp bảng Markdown để dán vào Google Sheets, hoặc copy khối **CSV** ngay bên dưới mỗi bảng và dùng tính năng **Data $\rightarrow$ Split text to columns (Tách văn bản thành cột)** trong Google Sheets.

---

## SHEET 1: `Items_20_Master` (Danh mục 20 Item Cốt lõi & Công thức Chế tạo)

### Bảng hiển thị trực quan
| Item_Code (Enum) | Item_ID | Cấp Độ (Tier) | Tên Hiển Thị | Ký Hiệu Khoa Học / Chuẩn | Phân Bố Địa Chất (`ESI` Range) | Công Thức Đầu Vào (Inputs) | Công Dụng Cốt Lõi & Cơ Chế Bắt Buộc |
| :--- | :---: | :---: | :--- | :---: | :---: | :--- | :--- |
| `RAW_WATER` | 1 | `RAW` | Băng Nước | $H_2O$ | `0.40 - 0.90` | Khai thác từ Mỏ | Nguyên liệu gốc cho Oxy, Khẩu phần ăn, Chất làm mát, Nhiên liệu. Đứt quá `30s` bị `WHO` cách ly. |
| `RAW_NITROGEN` | 2 | `RAW` | Khí Nitơ | $N_2$ | `0.50 - 1.00` | Khai thác từ Mỏ | Pha khí thở chống cháy nổ, làm dung dịch tải nhiệt và thuốc phóng đạn. |
| `RAW_METHANE` | 3 | `RAW` | Khí Metan | $CH_4$ | `0.20 - 0.55` | Khai thác từ Mỏ | Chế tạo nhiên liệu tên lửa hóa lỏng Methalox và luyện thép. |
| `RAW_BIOMASS` | 4 | `RAW` | Sinh Khối Hữu Cơ | $C$ | `0.70 - 1.00` | Khai thác từ Mỏ | Chế biến khẩu phần ăn nuôi nhân lực hành tinh và hạm đội. |
| `RAW_IRON` | 5 | `RAW` | Quặng Sắt | $Fe$ | `0.30 - 0.85` | Khai thác từ Mỏ | Luyện thép kết cấu công trình và phụ gia hợp kim giáp tàu. |
| `RAW_COPPER` | 6 | `RAW` | Quặng Đồng | $Cu$ | `0.30 - 0.80` | Khai thác từ Mỏ | Sản xuất vi mạch bán dẫn và trạm pin tích trữ điện năng. |
| `RAW_SILICON` | 7 | `RAW` | Thạch Anh / Silica | $SiO_2$ | `0.20 - 0.90` | Khai thác từ Mỏ | Tinh luyện tấm Wafer cho Chip bán dẫn, Radar và Pin dự phòng. |
| `RAW_TITANIUM` | 8 | `RAW` | Quặng Titan | $Ti$ | `0.15 - 0.45` | Khai thác từ Mỏ | Đúc giáp hợp kim nhẹ chịu lực cho tàu chiến (`FleetUnit`). |
| `RAW_TUNGSTEN` | 9 | `RAW` | Quặng Vonfram | $W$ ($3422^\circ\text{C}$) | `0.10 - 0.35` | Khai thác từ Mỏ | Chế tạo lõi đạn xuyên giáp động năng và vỏ bọc lò phản ứng hạt nhân. |
| `RAW_URANIUM` | 10 | `RAW` | Quặng Uranium | $U$ | `0.00 - 0.20` | Khai thác từ Mỏ | Nhiên liệu hạt nhân. **Cảnh báo:** Bị `IAEA` phát sóng vị trí toàn bản đồ khi khai thác/vận chuyển. |
| `ITEM_OXYGEN` | 11 | `REFINED` | Bình Khí Thở Nitrox | NASA ECLSS | Nhà máy | `1 RAW_WATER` + `1 RAW_NITROGEN` | Duy trì sự sống tại các Site có `ESI < 0.50` và cho `FleetUnit`. Thiếu hụt = Ngạt thở / `WHO` Quarantine. |
| `ITEM_RATIONS` | 12 | `REFINED` | Khẩu Phần Ăn MRE | NATO Class I | Nhà máy | `2 RAW_BIOMASS` + `1 RAW_WATER` | Nuôi nhân lực khai thác và thủy thủ đoàn trên đường tiếp vận. |
| `ITEM_COOLANT` | 13 | `REFINED` | Dung Dịch Tải Nhiệt | NASA TCS | Nhà máy | `1 RAW_NITROGEN` + `1 RAW_WATER` | Tản nhiệt trong chân không cho nhà máy và vũ khí khi bắn Salvo. Hết Coolant = Quá nhiệt tự nổ. |
| `ITEM_FUEL` | 14 | `REFINED` | Nhiên Liệu Methalox | NATO Class III | Nhà máy | `1 RAW_METHANE` + `1 RAW_WATER` | Nhiên liệu bắt buộc cho mọi `Convoys` và `FleetUnit` di chuyển trên `Starlanes`. |
| `ITEM_STEEL` | 15 | `REFINED` | Thép Kết Cấu | NATO Class IV | Nhà máy | `2 RAW_IRON` + `1 RAW_METHANE` | Xây dựng công trình cơ bản, giàn khoan và tàu vận tải dân sự (`Convoys`). |
| `ITEM_ALLOY` | 16 | `REFINED` | Giáp Hợp Kim Titan | Aerospace | Nhà máy | `2 RAW_TITANIUM` + `1 RAW_IRON` | Đóng tàu chiến (`Line`, `EW`, `Supply Tenders`) và nâng cấp công sự phòng thủ. |
| `ITEM_MICROCHIP` | 17 | `REFINED` | Chip Bán Dẫn & Cảm Biến | High-Tech | Nhà máy | `1 RAW_SILICON` + `1 RAW_COPPER` | Duy trì Trạm Radar, nâng cấp Tech TRL, mua dự báo bão `NOAA` và dẫn đường tên lửa. |
| `ITEM_BATTERY` | 18 | `REFINED` | Trạm Pin Dự Phòng | Grid Storage | Nhà máy | `2 RAW_COPPER` + `1 RAW_SILICON` | Giữ cho nhà máy và khiên chắn không bị sập nguồn khi Bão Địa Từ (`G1–G5`) quét qua. |
| `ITEM_AMMO` | 19 | `REFINED` | Đạn Xuyên Giáp Vonfram | NATO Class V | Nhà máy | `1 RAW_TUNGSTEN` + `1 RAW_NITROGEN` | Tiêu hao trực tiếp trong mỗi chu kỳ bắn (`Salvo`) của `FleetUnit`. Hết đạn = 0 sát thương. |
| `ITEM_NUCLEAR_CORE`| 20 | `REFINED` | Lõi Phản Ứng Hạt Nhân | Gen-IV Core | Nhà máy | `2 RAW_URANIUM` + `1 RAW_TUNGSTEN` | Nguồn năng lượng tối thượng cho Khiên hành tinh và Tàu chủ lực Tech Tier 5. Bị `IAEA` theo dõi. |

### Định dạng CSV (Copy vào Google Sheets)
```csv
Item_Code,Item_ID,Tier,Display_Name,Standard_Ref,ESI_Min,ESI_Max,Input_1,Qty_1,Input_2,Qty_2,Core_Mechanic
RAW_WATER,1,RAW,Băng Nước,H2O,0.40,0.90,NONE,0,NONE,0,Nguyên liệu gốc sự sống. Đứt quá 30s bị WHO cách ly 5 phút.
RAW_NITROGEN,2,RAW,Khí Nitơ,N2,0.50,1.00,NONE,0,NONE,0,Pha khí thở Nitrox + làm mát + thuốc phóng.
RAW_METHANE,3,RAW,Khí Metan,CH4,0.20,0.55,NONE,0,NONE,0,Chế tạo nhiên liệu Methalox và luyện thép.
RAW_BIOMASS,4,RAW,Sinh Khối Hữu Cơ,C,0.70,1.00,NONE,0,NONE,0,Chế biến khẩu phần ăn MRE nuôi nhân lực.
RAW_IRON,5,RAW,Quặng Sắt,Fe,0.30,0.85,NONE,0,NONE,0,Luyện thép kết cấu và phụ gia giáp tàu.
RAW_COPPER,6,RAW,Quặng Đồng,Cu,0.30,0.80,NONE,0,NONE,0,Sản xuất vi mạch bán dẫn và trạm pin dự phòng.
RAW_SILICON,7,RAW,Thạch Anh / Silica,SiO2,0.20,0.90,NONE,0,NONE,0,Tinh luyện Wafer cho Chip và Pin.
RAW_TITANIUM,8,RAW,Quặng Titan,Ti,0.15,0.45,NONE,0,NONE,0,Đúc giáp hợp kim nhẹ cho tàu chiến.
RAW_TUNGSTEN,9,RAW,Quặng Vonfram,W (3422C),0.10,0.35,NONE,0,NONE,0,Chế tạo đạn xuyên giáp và vỏ bọc lò hạt nhân.
RAW_URANIUM,10,RAW,Quặng Uranium,U,0.00,0.20,NONE,0,NONE,0,Nhiên liệu hạt nhân. Bị IAEA phát sóng vị trí toàn bản đồ.
ITEM_OXYGEN,11,REFINED,Bình Khí Thở Nitrox,NASA ECLSS,0.00,1.00,RAW_WATER,1,RAW_NITROGEN,1,Duy trì sự sống tại ESI < 0.50 và cho Fleet.
ITEM_RATIONS,12,REFINED,Khẩu Phần Ăn MRE,NATO Class I,0.00,1.00,RAW_BIOMASS,2,RAW_WATER,1,Nuôi nhân lực khai thác và thủy thủ đoàn.
ITEM_COOLANT,13,REFINED,Dung Dịch Tải Nhiệt,NASA TCS,0.00,1.00,RAW_NITROGEN,1,RAW_WATER,1,Tản nhiệt chân không cho nhà máy và vũ khí khi bắn Salvo.
ITEM_FUEL,14,REFINED,Nhiên Liệu Methalox,NATO Class III,0.00,1.00,RAW_METHANE,1,RAW_WATER,1,Nhiên liệu di chuyển bắt buộc trên Starlanes.
ITEM_STEEL,15,REFINED,Thép Kết Cấu,NATO Class IV,0.00,1.00,RAW_IRON,2,RAW_METHANE,1,Xây công trình cơ bản và đóng tàu buôn Convoys.
ITEM_ALLOY,16,REFINED,Giáp Hợp Kim Titan,Aerospace,0.00,1.00,RAW_TITANIUM,2,RAW_IRON,1,Đóng tàu chiến FleetUnit và công sự phòng thủ.
ITEM_MICROCHIP,17,REFINED,Chip Bán Dẫn & Cảm Biến,High-Tech,0.00,1.00,RAW_SILICON,1,RAW_COPPER,1,Duy trì Radar + nâng cấp Tech TRL + mua tin bão NOAA.
ITEM_BATTERY,18,REFINED,Trạm Pin Dự Phòng,Grid Storage,0.00,1.00,RAW_COPPER,2,RAW_SILICON,1,Chống sập nguồn nhà máy khi dính Bão Địa Từ G-Scale.
ITEM_AMMO,19,REFINED,Đạn Xuyên Giáp Vonfram,NATO Class V,0.00,1.00,RAW_TUNGSTEN,1,RAW_NITROGEN,1,Đạn tiêu hao mỗi Salvo giao chiến. Hết đạn = 0 sát thương.
ITEM_NUCLEAR_CORE,20,REFINED,Lõi Phản Ứng Hạt Nhân,Gen-IV Core,0.00,1.00,RAW_URANIUM,2,RAW_TUNGSTEN,1,Năng lượng cấp cao cho Khiên và Tàu Tier 5. Bị IAEA theo dõi.
```

---

## SHEET 2: `Time_And_Logistics_Constants` (Thông số Thời gian Thực 1:1 & Hậu cần)

### Bảng hiển thị trực quan
| Constant_Key | Giá Trị (`Ticks` / Số học) | Quy Đổi Thời Gian Thực | Hệ Thống Áp Dụng | Mô Tả Tác Động Gameplay |
| :--- | :---: | :---: | :---: | :--- |
| `TICKS_PER_SECOND` | `1` | `1 Giây` | `SimClock.cs` | Nhịp mô phỏng chuẩn 1:1 theo thời gian thực UTC. |
| `TICKS_PER_MINUTE` | `60` | `1 Phút` | `SimClock.cs` | Chu kỳ tính toán tiêu hao hậu cần cơ bản. |
| `TICKS_PER_HOUR` | `3,600` | `1 Giờ` | `SimClock.cs` | Chu kỳ tổng kết vĩ mô và biến động lớn. |
| `TICKS_PER_DAY` | `86,400` | `24 Giờ` | `SimClock.cs` | Chu kỳ ngày thực tế. |
| `TICKS_PER_WEEK` | `604,800` | `7 Ngày` | `SimClock.cs` | Giới hạn tối đa của chu kỳ Trophy / Nhiệm kỳ Hội đồng. |
| `DRILL_RETRACTION_DELAY_TICKS` | `180` | `3 Phút` | `FleetUnit.cs` | Thời gian chờ rút mũi khoan (`EDS`) trước khi tàu khai thác có thể chạy trốn bão. |
| `WTO_PRICE_UPDATE_INTERVAL` | `100` | `1 Phút 40 Giây` | `WtoMarketEngine.cs` | Chu kỳ cập nhật giá tham chiếu trung bình động (`VWAP/EMA`). |
| `WTO_PRICE_BAND_LIMIT` | `±20%` (`0.20`) | Tức thì | `WtoMarketEngine.cs` | Biên độ giá trần/sàn cho phép khi ký hợp đồng chính ngạch trên `WTO`. |
| `WTO_ESCROW_DEPOSIT_RATE` | `30%` (`0.30`) | Tức thì | `WtoMarketEngine.cs` | Tỷ lệ tiền đặt cọc khóa trong `WTO` để bảo vệ hợp đồng chống bùng hàng. |
| `WHO_DEPRIVATION_TRIGGER_TICKS`| `30` | `30 Giây` | `SystemAuthorityManager.cs`| Thời gian đứt `RAW_WATER` / `ITEM_OXYGEN` tối đa trước khi bị `WHO` phát lệnh cách ly. |
| `WHO_QUARANTINE_DURATION_TICKS`| `300` | `5 Phút` | `SystemAuthorityManager.cs`| Thời gian hành tinh bị khóa toàn bộ xuất khẩu do lệnh cách ly của `WHO`. |
| `VICTORY_MONOPOLY_THRESHOLD` | `70%` (`0.70`) | Tức thì | `VictoryEvaluator.cs` | Tỷ lệ độc quyền kinh tế cần đạt để thắng theo Con đường 1. |

### Định dạng CSV (Copy vào Google Sheets)
```csv
Constant_Key,Value_Numeric,Unit,Real_Time_Equivalent,Target_Script,Description
TICKS_PER_SECOND,1,Ticks,1 Second,SimClock.cs,Nhịp mô phỏng chuẩn 1:1 theo thời gian thực UTC.
TICKS_PER_MINUTE,60,Ticks,1 Minute,SimClock.cs,Chu kỳ tính toán tiêu hao hậu cần cơ bản.
TICKS_PER_HOUR,3600,Ticks,1 Hour,SimClock.cs,Chu kỳ tổng kết vĩ mô.
TICKS_PER_DAY,86400,Ticks,24 Hours,SimClock.cs,Chu kỳ ngày thực tế.
TICKS_PER_WEEK,604800,Ticks,7 Days,SimClock.cs,Giới hạn tối đa của chu kỳ Trophy.
DRILL_RETRACTION_DELAY_TICKS,180,Ticks,3 Minutes,FleetUnit.cs,Thời gian chờ rút mũi khoan (EDS) trước khi tàu có thể chạy bão.
WTO_PRICE_UPDATE_INTERVAL,100,Ticks,100 Seconds,WtoMarketEngine.cs,Chu kỳ cập nhật giá tham chiếu VWAP/EMA.
WTO_PRICE_BAND_LIMIT,0.20,Ratio,Immediate,WtoMarketEngine.cs,Biên độ giá trần/sàn (+-20%) trên sàn chính ngạch WTO.
WTO_ESCROW_DEPOSIT_RATE,0.30,Ratio,Immediate,WtoMarketEngine.cs,Tỷ lệ tiền đặt cọc khóa trong hợp đồng WTO (30%).
WHO_DEPRIVATION_TRIGGER_TICKS,30,Ticks,30 Seconds,SystemAuthorityManager.cs,Thời gian đứt Nước/Oxy tối đa trước khi bị WHO cách ly.
WHO_QUARANTINE_DURATION_TICKS,300,Ticks,5 Minutes,SystemAuthorityManager.cs,Thời gian khóa xuất khẩu hành tinh khi bị WHO Quarantine.
VICTORY_MONOPOLY_THRESHOLD,0.70,Ratio,Immediate,VictoryEvaluator.cs,Tỷ lệ độc quyền thị trường cần đạt để chiến thắng kinh tế.
```

---

## SHEET 3: `NOAA_Space_Weather_Scales` (Thang đo Bão Vũ trụ NOAA & Tác động)

### Bảng hiển thị trực quan
| Loại Bão (Scale) | Cấp Độ | Tên Khoa Học (NOAA) | Tốc Độ Lan Truyền | Điều Kiện Chống Chịu | Tác Động Lên Điều Khiển, Quân Cờ & Kinh Tế |
| :--- | :---: | :--- | :---: | :--- | :--- |
| **`R-Scale`** | `R1 - R2` | Radio Blackout (Minor/Moderate) | Tốc độ ánh sáng (Tới trước tiên) | Radar Cấp 2+ | Nhiễu tín hiệu nhẹ; tăng độ trễ khi gửi lệnh `Live Command Override` vào trận đánh. |
| **`R-Scale`** | `R3 - R5` | Radio Blackout (Strong/Extreme) | Tốc độ ánh sáng (Tới trước tiên) | Không thể xuyên qua | **Mù Radar cục bộ & Khóa hoàn toàn quyền gửi lệnh điều khiển / khóa nút rút lui `[EVACUATE]`** đối với mọi tàu trong tâm bão. |
| **`S-Scale`** | `S1 - S2` | Solar Radiation Storm (Minor/Mod) | Tới sau `R-Scale` | `Tech Tier >= S-Level` | Gây hao hụt nhân lực nhẹ nếu tàu/trạm khai thác có `Tech Tier < 2`. |
| **`S-Scale`** | `S3 - S5` | Solar Radiation Storm (Severe/Ext)| Tới sau `R-Scale` | `Tech Tier >= S-Level` + Giáp/Khiên | Phá hủy độ bền giàn khoan và trừ thẳng vào máu/nhân lực của `FleetUnit` nếu `Tech Tier < S-Level`. |
| **`G-Scale`** | `G1 - G2` | Geomagnetic Storm (Minor/Mod) | Tới cùng đám mây CME | Trạm `ITEM_BATTERY` | Giảm tốc độ di chuyển trên `Starlanes`; gây sụt áp nhẹ tại nhà máy tinh luyện. |
| **`G-Scale`** | `G3 - G5` | Geomagnetic Storm (Strong/Ext) | Tới cùng đám mây CME | Tiêu thụ mạnh `ITEM_BATTERY` | **Đánh sập lưới điện nhà máy tinh luyện** (ngừng sản xuất nếu không có `ITEM_BATTERY` dự phòng) và làm sập khiên chắn hành tinh. |

### Định dạng CSV (Copy vào Google Sheets)
```csv
Scale_Type,Level_Range,NOAA_Classification,Arrival_Order,Mitigation_Requirement,Gameplay_Effect
R-Scale,R1-R2,Radio Blackout (Minor-Moderate),1st (Speed of Light),Radar Tier 2+,Tăng độ trễ lệnh điều khiển Live Command Override.
R-Scale,R3-R5,Radio Blackout (Strong-Extreme),1st (Speed of Light),None (Total Blackout),Mù Radar cục bộ; KHÓA hoàn toàn lệnh rút lui [EVACUATE] và lệnh can thiệp trận đánh.
S-Scale,S1-S2,Solar Radiation Storm (Minor-Mod),2nd (Energetic Particles),Tech Tier >= S-Level,Hao hụt nhân lực/độ bền nhẹ nếu Tech Tier thấp hơn cấp bão.
S-Scale,S3-S5,Solar Radiation Storm (Severe-Ext),2nd (Energetic Particles),Tech Tier >= S-Level,Trừ mạnh độ bền giàn khoan và máu FleetUnit nếu Tech Tier < S-Level.
G-Scale,G1-G2,Geomagnetic Storm (Minor-Mod),3rd (CME Plasma Cloud),ITEM_BATTERY Reserve,Giảm tốc độ bay trên Starlanes; sụt áp nhẹ tại nhà máy.
G-Scale,G3-G5,Geomagnetic Storm (Strong-Ext),3rd (CME Plasma Cloud),Heavy ITEM_BATTERY Drain,Đánh sập nhà máy tinh luyện (nếu hết Pin dự phòng) và làm sập khiên chắn.
```

---

## SHEET 4: `Starlanes_And_Fleet_Combat` (Tuyến đường, Quân cờ & Can thiệp Giao chiến)

### Bảng hiển thị trực quan
| Phân Nhóm | Mã Thành Phần / Cơ Chế | Chi Phí / Đầu Vào | Thông Số Kỹ Thuật | Vai Trò & Quy Tắc Tương Tác Trong Game |
| :--- | :--- | :--- | :---: | :--- |
| **Tuyến Đường** | `Highway_STA` (Cao tốc STA) | Nộp phí `Credits` / `% Hàng` | `1.0x Fuel`, Bão `G0/S0` | Nhanh và an toàn nhất; dùng cho 90% hoạt động thương mại và tiếp viện chính quy. |
| **Tuyến Đường** | `Outer_Rim_Bypass` (Đường vòng) | `0 Credits` (Miễn phí) | `4.0x - 5.0x Fuel`, Bão `G1-G2` | Rất tốn nhiên liệu và đi lâu; dùng khi bị `STA` cấm vận hoặc đi vòng đánh úp lén lút. |
| **Tuyến Đường** | `Hazard_Shortcut` (Xuyên tâm bão) | `0 Credits` (Miễn phí) | `1.5x Fuel`, Bão `G4/S4/R4` | Đường tắt nguy hiểm; chỉ dành cho tàu `Tech Tier 4-5` hoặc buôn lậu `RAW_URANIUM` né `IAEA`. |
| **Thành Phần Quân Cờ** | `Line_Ships` (Tàu Chiến tuyến) | `ITEM_ALLOY` + `ITEM_STEEL` | Hỏa lực & Giáp chính | Đóng vai trò chịu đòn và bắn đạn xuyên giáp `ITEM_AMMO` theo phương trình Lanchester. |
| **Thành Phần Quân Cờ** | `EW_Vessels` (Tác chiến Điện tử) | `ITEM_ALLOY` + `ITEM_MICROCHIP` | Đánh chặn & Radar | Bắn hạ tên lửa bay tới (`Point-Defense`), gây nhiễu đối phương và mở rộng tầm nhìn Radar. |
| **Thành Phần Quân Cờ** | `Supply_Tenders` (Tàu Hậu cần) | `ITEM_STEEL` + Khoang chứa | Kho tiếp vận + Giàn khoan | Chứa `ITEM_AMMO`, `ITEM_FUEL`, `ITEM_COOLANT`, `ITEM_OXYGEN` và mũi khoan khai thác (rút neo mất `180s`). |
| **Lệnh Giữa Trận** | `Target_Priority_Override` | Tín hiệu Radio (`< R3`) | Chọn: `Line` / `EW` / `Supply` | Ra lệnh cho hạm đội tập trung hỏa lực vào 1 trong 3 thành phần của địch (VD: bắn `Supply` để cắt đạn/chặn `Hot-Drop`). |
| **Lệnh Giữa Trận** | `Fire_Rate_Override` | Tín hiệu Radio (`< R3`) | Chọn: `50%` / `100%` / `150%` | Ép xung tốc độ bắn lên `150%` (tốn gấp đôi đạn và tăng nhiệt cực nhanh) hoặc hạ xuống `50%` để tiết kiệm đạn. |
| **Lệnh Giữa Trận** | `Coolant_Valve_Override` | Tín hiệu Radio (`< R3`) | Bật / Tắt xả `ITEM_COOLANT` | Xả trực tiếp `ITEM_COOLANT` để hạ nhiệt khẩn cấp khi ép xung hỏa lực `150%`. |
| **Hậu Cần Chiến Trường**| `Hot_Drop_Resupply` (Tiếp tế nóng) | Đoàn tàu `Convoys` | Chở Đạn / Coolant / Pin | Lao thẳng vào điểm đang giao chiến để bơm vật tư trực tiếp; bị bắn rụng nếu địch đang ưu tiên ngắm vào `Supply`. |

### Định dạng CSV (Copy vào Google Sheets)
```csv
Category,Component_Code,Cost_Or_Input,Key_Spec,Gameplay_Rule
Starlane,Highway_STA,STA Toll (Credits/%),1.0x Fuel | G0/S0 Safe,Tuyến cao tốc an toàn và nhanh nhất; chịu sự kiểm soát của STA.
Starlane,Outer_Rim_Bypass,0 Credits,4.0x-5.0x Fuel | G1-G2 Storm,Tuyến đi vòng miễn phí nhưng cực tốn xăng; dùng khi bị cấm vận hoặc đánh úp.
Starlane,Hazard_Shortcut,0 Credits,1.5x Fuel | G4/S4/R4 Extreme,Tuyến cắt ngang tâm bão; dành cho tàu Tech Tier 4-5 hoặc buôn lậu Uranium né IAEA.
Fleet_SubUnit,Line_Ships,ITEM_ALLOY + ITEM_STEEL,Main Armor & Salvo DPS,Tàu chiến tuyến chịu đòn và gây sát thương chính bằng ITEM_AMMO.
Fleet_SubUnit,EW_Vessels,ITEM_ALLOY + ITEM_MICROCHIP,Point-Defense & Jamming,Tàu tác chiến điện tử đánh chặn tên lửa và soi Radar.
Fleet_SubUnit,Supply_Tenders,ITEM_STEEL + Cargo Bay,Logistics & Integrated Drill,Chứa Đạn/Fuel/Coolant/Oxy và mũi khoan mỏ (thu hồi mất 180 Ticks).
Live_Command,Target_Priority_Override,Radio Signal (< R3),Line | EW | Supply,Chuyển mục tiêu ưu tiên giữa trận. Nhắm vào Supply để diệt tàu tiếp tế Hot-Drop.
Live_Command,Fire_Rate_Override,Radio Signal (< R3),50% | 100% | 150%,Điều chỉnh cường độ bắn Salvo. Mức 150% gây quá nhiệt nhanh và tốn đạn.
Live_Command,Coolant_Valve_Override,Radio Signal (< R3),Valve ON / OFF,Xả ITEM_COOLANT trực tiếp giữa trận để chống nổ tàu do quá nhiệt.
Combat_Logistics,Hot_Drop_Resupply,Trade Convoys,Ammo + Coolant + Battery,Điều tàu vận tải lao thẳng vào trận đánh để bơm đạn và chất làm mát trực tiếp.
```

---

## SHEET 5: `System_Agencies_And_Bounty` (5 Cơ quan `THE SYSTEM`, Chức danh & Bậc Bồi hoàn)

### Bảng hiển thị trực quan
| Phân Loại | Mã Định Danh | Tên Đầy Đủ | Quyền Hạn Vật Lý / Cơ Chế Thực Thi Tự Động |
| :--- | :--- | :--- | :--- |
| **Cơ Quan Chấp Pháp** | `STA` | Space Transit Authority | Thu phí tuyến cao tốc; khóa cổng giao thông đối với người chơi vi phạm hoặc nợ phí. |
| **Cơ Quan Chấp Pháp** | `WTO` | World/Galactic Trade Org | Cập nhật giá `VWAP/EMA` mỗi `100s` (biên độ `±20%`); giữ tiền cọc `Escrow 30%` bảo đảm hợp đồng. |
| **Cơ Quan Chấp Pháp** | `WHO` | Health/Bio-Safety Org | Tự động khóa xuất khẩu `5 phút` (`Quarantine`) nếu hành tinh đứt `RAW_WATER`/`ITEM_OXYGEN` quá `30s`. |
| **Cơ Quan Chấp Pháp** | `IAEA` | Atomic Energy Agency | Phát sóng công khai vị trí mọi hành tinh/đoàn tàu khai thác hoặc vận chuyển `RAW_URANIUM` & `ITEM_NUCLEAR_CORE`. |
| **Cơ Quan Chấp Pháp** | `NOAA` | Space Weather Agency | Bán dữ liệu dự báo bão vũ trụ sớm độc quyền (thu `Credits` hoặc `ITEM_MICROCHIP`). |
| **Bồi Hoàn Phòng Vệ** | `Tier_1_Bounty` | Bậc Hỗ Trợ Cơ Bản | Mức bồi hoàn thiệt hại / chia thưởng **`30%`** khi tham gia bảo vệ `THE SYSTEM`. |
| **Bồi Hoàn Phòng Vệ** | `Tier_2_Bounty` | Bậc Hỗ Trợ Trung Cấp | Mức bồi hoàn thiệt hại / chia thưởng **`65%`** khi tham gia bảo vệ `THE SYSTEM`. |
| **Bồi Hoàn Phòng Vệ** | `Tier_3_Bounty` | Bậc Hỗ Trợ Cao Cấp | Mức bồi hoàn thiệt hại / chia thưởng **`90%`** khi tham gia bảo vệ `THE SYSTEM`. |
| **Bồi Hoàn Phòng Vệ** | `Tier_4_Bounty` | Bậc Hỗ Trợ Toàn Phần | Mức bồi hoàn thiệt hại / chia thưởng **`100%`** khi tham gia bảo vệ `THE SYSTEM` ở ngưỡng khẩn cấp. |
| **Hội Đồng Quản Trị** | `ChiefAdministrator`| Tổng Quản Trị Viên (`CA`) | Bầu cử định kỳ sau khi lật đổ `THE SYSTEM`; nắm quyền Veto và phân bổ ngân sách trung ương. |
| **Hội Đồng Quản Trị** | `Director_Agencies` | Giám Đốc 5 Cơ Quan | Gồm 5 ghế: `DirectorOfSTA`, `DirectorOfWTO`, `DirectorOfWHO`, `DirectorOfIAEA`, `DirectorOfNOAA`. |

### Định dạng CSV (Copy vào Google Sheets)
```csv
Group_Type,Code_ID,Full_Name,System_Mechanic_Description
System_Agency,STA,Space Transit Authority,Thu phí tuyến cao tốc; khóa cổng giao thông đối với kẻ vi phạm.
System_Agency,WTO,World/Galactic Trade Organization,Tính giá VWAP/EMA mỗi 100 Ticks (+-20%); giữ cọc Escrow 30%.
System_Agency,WHO,Health/Bio-Safety Organization,Khóa xuất khẩu 300 Ticks (Quarantine) nếu đứt Nước/Oxy quá 30 Ticks.
System_Agency,IAEA,Atomic Energy Agency,Phát sóng công khai vị trí mọi nơi có RAW_URANIUM và ITEM_NUCLEAR_CORE.
System_Agency,NOAA,Space Weather Agency,Bán bản tin dự báo bão R/S/G sớm bằng Credits hoặc ITEM_MICROCHIP.
Defense_Bounty,Bounty_Tier_1,30% Reimbursement Tier,Bồi hoàn và chia thưởng mức 30% theo SystemDefenseEvaluator.
Defense_Bounty,Bounty_Tier_2,65% Reimbursement Tier,Bồi hoàn và chia thưởng mức 65% theo SystemDefenseEvaluator.
Defense_Bounty,Bounty_Tier_3,90% Reimbursement Tier,Bồi hoàn và chia thưởng mức 90% theo SystemDefenseEvaluator.
Defense_Bounty,Bounty_Tier_4,100% Reimbursement Tier,Bồi hoàn và chia thưởng mức 100% theo SystemDefenseEvaluator.
Directorate_Role,ChiefAdministrator,Chief Administrator (CA),Tổng Quản Trị Viên (Enum = 1) - Nắm quyền Veto và Ngân sách sau khi lật đổ THE SYSTEM.
Directorate_Role,DirectorOfSTA,Director of STA,Giám đốc Giao thông & Trạm thu phí (Enum = 2).
Directorate_Role,DirectorOfWTO,Director of WTO,Giám đốc Thương mại & Ký quỹ Escrow (Enum = 3).
Directorate_Role,DirectorOfWHO,Director of WHO,Giám đốc Kiểm dịch Sinh học (Enum = 4).
Directorate_Role,DirectorOfIAEA,Director of IAEA,Giám đốc Giám sát Hạt nhân (Enum = 5).
Directorate_Role,DirectorOfNOAA,Director of NOAA,Giám đốc Khí tượng Vũ trụ & Cảnh báo Bão (Enum = 6).
```