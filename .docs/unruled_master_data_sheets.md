# UNRULED — Master Data Sheets (v3.0)

> Exported from `UNRULED — Master Data Sheets (v3.0 - 6 Tabs).xlsx`. The xlsx is the source; re-export after editing it.

## 01_Items_20_Recipes

| Item_ID | Item_Code | Category | Name_VN | Preferred_ESI_Range | Input_1_Code | Input_1_Qty | Input_2_Code | Input_2_Qty | Base_WTO_Price | System_And_Tactical_Role |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | RAW_WATER | 1_RAW | Băng Nước (H2O) | 0.40 - 0.90 | NONE | 0 | NONE | 0 | 10 | Nguyên liệu Oxy / Xăng Methalox / Chất làm mát |
| 2 | RAW_NITROGEN | 1_RAW | Khí Nitơ (N2) | 0.50 - 1.00 | NONE | 0 | NONE | 0 | 12 | Nguyên liệu Khí thở / Thuốc phóng / Làm mát |
| 3 | RAW_METHANE | 1_RAW | Khí Metan (CH4) | 0.20 - 0.55 | NONE | 0 | NONE | 0 | 15 | Nguyên liệu Xăng tên lửa & Luyện thép kết cấu |
| 4 | RAW_BIOMASS | 1_RAW | Sinh Khối Hữu Cơ | 0.70 - 1.00 | NONE | 0 | NONE | 0 | 18 | Nguyên liệu Lương thực (Bị xóa sổ 100% nếu dính Hạt nhân) |
| 5 | RAW_IRON | 1_RAW | Quặng Sắt (Fe) | 0.30 - 0.85 | NONE | 0 | NONE | 0 | 14 | Nguyên liệu Thép kết cấu Tier 1-2 |
| 6 | RAW_COPPER | 1_RAW | Quặng Đồng (Cu) | 0.30 - 0.80 | NONE | 0 | NONE | 0 | 20 | Nguyên liệu Vi mạch & Trạm Pin dự phòng |
| 7 | RAW_SILICON | 1_RAW | Thạch Anh (SiO2) | 0.20 - 0.90 | NONE | 0 | NONE | 0 | 22 | Nguyên liệu Chip bán dẫn & Cảm biến Radar |
| 8 | RAW_TITANIUM | 1_RAW | Quặng Titan (Ti) | 0.15 - 0.45 | NONE | 0 | NONE | 0 | 45 | Nguyên liệu Giáp hợp kim chịu lực Tier 3+ |
| 9 | RAW_TUNGSTEN | 1_RAW | Quặng Vonfram (W) | 0.10 - 0.35 | NONE | 0 | NONE | 0 | 65 | Nguyên liệu Đạn xuyên giáp & Mũi khoan lõi sâu |
| 10 | RAW_URANIUM | 1_RAW | Quặng Uranium (U) | 0.00 - 0.20 | NONE | 0 | NONE | 0 | 150 | Nguyên liệu Hạt nhân (Bị IAEA phát sóng vị trí toàn bản đồ) |
| 11 | ITEM_OXYGEN | 2_REFINED | Bình Khí Thở Nitrox | N/A | RAW_WATER | 1 | RAW_NITROGEN | 1 | 30 | Duy trì sự sống (Đứt cung >30 Ticks bị WHO cách ly 5 phút) |
| 12 | ITEM_RATIONS | 2_REFINED | Khẩu Phần Ăn MRE | N/A | RAW_BIOMASS | 2 | RAW_WATER | 1 | 55 | Nuôi thủy thủ đoàn & Công nhân mỏ |
| 13 | ITEM_COOLANT | 2_REFINED | Dung Dịch Tải Nhiệt | N/A | RAW_NITROGEN | 1 | RAW_WATER | 1 | 35 | Xả nhiệt pháo Salvo (<90°C) & Nuôi tàu Tier 3-5 |
| 14 | ITEM_FUEL | 2_REFINED | Nhiên Liệu Methalox | N/A | RAW_METHANE | 1 | RAW_WATER | 1 | 38 | Chạy tàu Starlanes (Tốn 4.5x khi luồn Vành Đai) |
| 15 | ITEM_STEEL | 2_REFINED | Thép Kết Cấu | N/A | RAW_IRON | 2 | RAW_METHANE | 1 | 50 | Đóng & Sửa tàu Tier 1-2 / Xây công trình cơ bản |
| 16 | ITEM_ALLOY | 2_REFINED | Giáp Hợp Kim Titan | N/A | RAW_TITANIUM | 2 | RAW_IRON | 1 | 125 | Đóng & Bảo trì tàu Tier 3-5 / Đóng gói Chuyển Base |
| 17 | ITEM_MICROCHIP | 2_REFINED | Chip Bán Dẫn & Radar | N/A | RAW_SILICON | 1 | RAW_COPPER | 1 | 60 | Quét Deep Telemetry / Nghiên cứu TRL / Bảo trì Tier 4 |
| 18 | ITEM_BATTERY | 2_REFINED | Trạm Pin Dự Phòng | N/A | RAW_COPPER | 2 | RAW_SILICON | 1 | 80 | Chống bão G-Scale / Quét Radar Vành Đai (2 Pin/lần) |
| 19 | ITEM_AMMO | 2_REFINED | Đạn Xuyên Giáp Vonfram | N/A | RAW_TUNGSTEN | 1 | RAW_NITROGEN | 1 | 95 | Hỏa lực Salvo chính & Pháo kích công thành |
| 20 | ITEM_NUCLEAR_CORE | 2_REFINED | Lõi Phản Ứng Hạt Nhân | N/A | RAW_URANIUM | 2 | RAW_TUNGSTEN | 1 | 420 | Đóng tàu Tier 5 & Vũ khí Hạt nhân hủy diệt hành tinh |

## 02_Planetary_Lifecycle

| State_ID | State_Code | Trigger_Condition | Ore_Output_Multiplier | Drill_Fuel_Coolant_Cost | ESI_Modifier | Inherent_Radiation_Hazard | Strategic_Value_And_Special_Rules |
|---|---|---|---|---|---|---|---|
| 1 | PRISTINE_WORLD | Mới sinh từ SystemSeed (Surface Vein > 0%) | 100% Speed | 1.0x FUEL / 1.0x COOLANT | +0.00 (Original ESI) | None | Khai thác nhanh và rẻ nhất đầu game |
| 2 | DEEP_CORE_STRIPPING | Hết Surface Vein -> Khoan tầng Deep Crust | 50% Speed | 2.0x FUEL / 2.0x COOLANT | -0.05 ESI | None | Trụ cột kinh tế giữa game; rút neo mất 180 Ticks (3 phút) |
| 3A | DEPLETED_BARREN_HUSK | Hết 100% cả Surface lẫn Deep Crust | 0% (Vĩnh viễn hết quặng) | N/A (Ngừng đào) | -0.20 ESI (Sụt lún địa chất) | None | Không xóa Node! Dùng làm Kho trung chuyển / Trạm Tiếp Sóng Relay / Pháo đài |
| 3B | IRRADIATED_DEAD_WORLD | Bị bắn ITEM_NUCLEAR_CORE khi công thành | Mở khóa Vỉa Lõi Sâu (W / Ti / U) | 4.0x FUEL / 4.0x COOLANT | ESI = 0.00 (Xóa sổ 100% Biomass) | Phát xạ = Bão S4 (24h-72h) | Bị IAEA/WHO đánh dấu Đỏ; nứt vỏ lộ quặng quý nhưng chỉ tàu Tier 4-5 vào đào được |
| 4 | TERRAFORMED_RECOVERY | Người chơi Tier 5 bơm Water+N2+Biomass+Nuclear | Không phục hồi mỏ đã cạn | N/A | Kéo ESI từ 0.00 -> 0.60 | Khử sạch bức xạ S4 | Phục hồi hành tinh chết thành trung tâm dân cư / công nghiệp cuối game |

## 03_Ships_And_Upkeep

| Ship_Tech_Tier | Build_And_Repair_Items | Active_Upkeep_Per_Hour | Combat_Burn_Multiplier | Mothball_Upkeep_Discount | Mothball_Warmup_Delay_Ticks | IAEA_Global_Reveal | Optimal_High_Low_Mix_Role |
|---|---|---|---|---|---|---|---|
| Tier_1_Basic | ITEM_STEEL | 1x OXYGEN + 1x RATIONS | 1.0x AMMO / 1.0x FUEL | 80% Off | 60 Ticks (1 Phút) | FALSE | Canh mỏ an toàn / Tuần tra bắt tàu buôn / Bia đỡ đạn giá rẻ |
| Tier_2_Standard | ITEM_STEEL + ITEM_BATTERY | 1x OXYGEN + 1x RATIONS + 0.5x STEEL | 1.2x AMMO / 1.2x FUEL | 80% Off | 90 Ticks (1.5 Phút) | FALSE | Lực lượng phòng thủ biên giới & Hộ tống đoàn tàu tiếp vận |
| Tier_3_Specialized | ITEM_ALLOY + ITEM_AMMO | 2x OXYGEN + 2x COOLANT + 1x ALLOY | 2.0x AMMO / 2.0x COOLANT | 80% Off | 120 Ticks (2 Phút) | FALSE | Mở khóa phân nhánh chuyên biệt (Railgun / Brawler / Aegis) |
| Tier_4_HighTech | ITEM_ALLOY + ITEM_MICROCHIP | 3x COOLANT + 2x BATTERY + 1x MICROCHIP | 2.5x AMMO / 2.5x COOLANT / 2x BATTERY | 80% Off | 150 Ticks (2.5 Phút) | FALSE | Soái hạm Laser xuyên bão R-Scale & Đi đường tắt xuyên bão G4/S4 |
| Tier_5_ApexNuclear | ITEM_NUCLEAR_CORE + ITEM_ALLOY | 5x COOLANT + 2x ALLOY + 2x MICROCHIP | 3.5x AMMO / 4.0x COOLANT | 80% Off | 180 Ticks (3 Phút) | TRUE (Lộ vị trí 24/7) | Vũ khí chiến lược công phá System Citadel & Thủ đô địch |

## 04_Routes_Radar_Latency

| System_Category | Mechanic_Parameter | Formula_Or_Value | Resource_Cost | Technical_Evidence_And_Gameplay_Rule |
|---|---|---|---|---|
| Starlane_Tier_1 | STA_Toll_Highway | Speed 100% \| Fuel 1.0x \| Storm G0/S0 | Credits Toll to STA | Nhanh & An toàn nhưng Lộ diện 100% trên Radar cơ bản |
| Starlane_Tier_2 | Outer_Rim_Asteroid_Bypass | Speed 60% \| Fuel 4.5x \| Storm G1-G2 | 0 Credits (High ITEM_FUEL) | Ẩn 90% tín hiệu trước Radar tự động (Volume Clutter - IEEE Std 686) |
| Starlane_Tier_3 | Hazard_Storm_Shortcut | Speed 90% \| Fuel 1.5x \| Storm G4/S4/R4 | 0 Credits (Req Tech Tier 4-5) | Cắt ngang qua tâm bão để đánh úp bất ngờ |
| Focused_Radar | Startup_Surge_Cost | Deducted Instantly on Click | 2x ITEM_BATTERY | Chống trò Bật/Tắt Radar nháy liên tục để soi trộm Vành Đai |
| Focused_Radar | Coherent_Integration_Time | 60 Ticks (60 Real Seconds) | Continuous Power | Phải giữ tia quét đủ 60s mới hiện rõ quân địch; tắt sớm mất trắng Pin |
| Focused_Radar | RWR_Stealth_Early_Warning | Triggers at 15s (45s before reveal) | Requires EwShips in Fleet | Báo động cho quân đang nấp trong Vành Đai để Quay đầu hoặc Bật Jamming (120s) |
| Signal_Latency | At_Core_Base_Cluster | Latency = 15 Ticks (15 Seconds) | 0 Extra Cost | Tâm tính độ trễ gốc (0 Hops từ Core Base hiện tại) |
| Signal_Latency | Distant_No_Relay_Chain | Latency = 15 + (Hops * 35 Ticks) | 0 Extra Cost | Suy hao tín hiệu không gian sâu khi đánh xa nhà mà không có trạm tiếp sóng |
| Signal_Latency | Distant_With_Relay_Chain | Latency = 15 + (Hops * 8 Ticks) | EwShips / Relay Battery Upkeep | Nhanh hơn không có Relay nhưng luôn chậm hơn Core Base (ITU-T G.114) |
| Base_Relocation | Phase_1_Teardown_Old_Base | 900 - 1800 Ticks (15 - 30 Mins) | ALLOY + FUEL + BATTERY | Base cũ giảm 50% SiteDefenseBonus (Khóa nút nếu đang bị vây đánh) |
| Base_Relocation | Phase_2_Capital_Convoy | 1800 - 3600 Ticks (30 - 60 Mins) | Escort Fleet Required | Độ trễ lệnh toàn quân x2; nếu tàu Chuyển Đô bị bắn nổ -> Tê liệt lệnh 15 phút |
| Base_Relocation | Phase_3_Bootstrap_New_Base | 1800 Ticks (30 Mins) \| Cooldown 48h | COOLANT + BATTERY | Đồng bộ mạng lưới và chuyển tâm tính Độ trễ 15s về Base mới |
| Combat_Ratio | In_Transit_1v1_Encounter | Attacker == Defender (No Fortification) | Standard Ammo/Coolant | Kết quả HÒA (Stalemate) trên đường bay |
| Combat_Ratio | Planetary_1v1_Siege | Defender +35% PD & Planet Vault Access | Zero-Distance Resupply | Phe Thủ THẮNG 100% nếu Phe Công chỉ đâm 1 đường với tỷ lệ 1:1 |
| Combat_Ratio | Multi_Lane_Flanking_Bonus | -20% Defender PD per extra attack lane | Multi-Route Coordination | Buộc người chơi phải chia quân đánh kẹp từ 2-3 đường cùng lúc như AoE |

## 05_Leaderboard_Top10K

| Bracket_ID | Bracket_Name | Rank_Range_Top10K | Percentile_Cap | WTO_Escrow_Rate | WTO_Market_Fee | WTO_Dividend_Pool_Share | STA_Toll_Discount | STA_Upkeep_Grant_Share | NOAA_Storm_Warning_Lead |
|---|---|---|---|---|---|---|---|---|---|
| 6 | Apex_Tier0 | Rank 1 - 10 | Top 0.1% | 15% (Default 30%) | 2.0% (Default 5%) | 15% of Tax Pool | 10% Off | 15% (FUEL + COOLANT + ALLOY) | +30 Minutes Free |
| 5 | Superpower_AAA_S | Rank 11 - 100 | Top 1.0% | 18% | 2.5% | 25% of Tax Pool | 8% Off | 25% (FUEL + COOLANT + STEEL) | +20 Minutes Free |
| 4 | HighGrade_AA_A | Rank 101 - 500 | Top 5.0% | 21% | 3.0% | 25% of Tax Pool | 6% Off | 25% (FUEL + STEEL) | +12 Minutes Free |
| 3 | MidGrade_A_B | Rank 501 - 2000 | Top 20.0% | 24% | 3.8% | 20% of Tax Pool | 4% Off | 20% (ITEM_FUEL) | +5 Minutes Free |
| 2 | Standard_BBB_C | Rank 2001 - 5000 | Top 50.0% | 26% | 4.2% | 10% of Tax Pool | 2% Off | 10% (Basic ITEM_FUEL) | Current Storm Path |
| 1 | Registered_BB_D | Rank 5001 - 10000 | Top 100.0% | 28% | 4.5% | 5% of Tax Pool | 1% Off | 5% (Minor FUEL + OXYGEN) | Current Storm Path |
| 0 | Unrated_Ghost | Not Published / >10000 | N/A | 30% (40% if evading) | 5.0% | 0% | 0% Off | 0% (100% Self-Funded) | 0 (Keep 100% Tech Tier Secrecy!) |

## 06_Utility_AI_Presets

| Archetype_ID | Preset_Code | Defense_Hold_W | Evacuate_Safety_W | Radar_Belt_Scan_W | Resupply_Logistics_W | Stealth_Raid_W | Siege_Conquest_W | Coolant_Vent_Temp_C | Retreat_Force_Ratio | Min_Battery_For_Scan | Evacuate_Storm_Level |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | Sentinel_Defender | 1.8 | 1.7 | 1.5 | 1.4 | 0.0 | 0.0 | 75°C | 0.65 | 30% | G3 / S3 |
| 2 | Industrialist_Trader | 1.2 | 1.9 | 1.0 | 2.0 | 0.0 | 0.2 | 70°C | 0.80 | 50% | G3 / S3 |
| 3 | Raider_Ambush | 0.6 | 1.4 | 1.2 | 1.0 | 1.9 | 0.5 | 85°C | 0.75 | 40% | G4 / S4 |
| 4 | Warlord_Conqueror | 1.0 | 0.7 | 1.1 | 1.5 | 1.1 | 1.8 | 88°C | 0.35 | 35% | G5 / S5 |
| 5 | Technocrat_Explorer | 1.2 | 1.2 | 1.8 | 1.3 | 0.8 | 1.0 | 80°C | 0.50 | 25% | G4 / S4 |
