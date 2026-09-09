// ============================================================================
// MGAMCTSTopologySelector.cs  (从 MGAAdaptiveSolver.cs 拆分, P1)
// 权威自适应 MGA 求解器子模块。底层 100% 官方 ESA kep3 Native C++ 闭环动力学。
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KSPGMAT.Model;

namespace KSPGMAT.MGA
{
    /// <summary>
    /// 拓扑搜索超参数模型 (由开普勒轨道特征自适应推导演变)
    /// </summary>
    public struct TopologyHyperparameters
    {
        public int MaxSwingBys;
        public int MaxResonances;
        public int MaxBackLegs;
        public int MaxBackSpacing;
        public double OrbitRadiusRatio;
        public string Rationale;

        public override string ToString()
        {
            return $"MaxSwingBys={MaxSwingBys}, MaxResonances={MaxResonances}, MaxBackLegs={MaxBackLegs}, MaxBackSpacing={MaxBackSpacing} ({Rationale})";
        }
    }

    /// <summary>
    /// 模块 C: 基于蒙特卡洛树搜索 (MCTS) 的离散重力助推链剪枝生成器与拓扑参数自寻优引擎
    /// 理论来源: Daniel Hennes & Dario Izzo (IJCAI 2015) "Interplanetary Trajectory Planning with MCTS"
    /// 机制: 确定性求值 max 估计器、Contraction 全覆盖子树剪除、10 km/s 硬可行性门禁、天体力学势能梯度自推导
    /// </summary>
    public static class MGAMCTSTopologySelector
    {
        /// <summary>
        /// 基于天体力学引力井深度与半长轴势能梯度，全自动求取最优拓扑超参数四元组 (零人工干预)
        /// </summary>
        public static TopologyHyperparameters AutoTuneTopologyHyperparameters(string departurePlanet, string arrivalPlanet)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);

            double rDep = (pDep != null && pDep.SMA > 0) ? pDep.SMA : 1.496e8;
            double rArr = (pArr != null && pArr.SMA > 0) ? pArr.SMA : 1.496e8;

            double ratio = Math.Max(rDep, rArr) / Math.Max(1.0, Math.Min(rDep, rArr));
            bool isDeepInner = (rArr < rDep * 0.5); // 如水星 (0.387 AU vs 1.0 AU, ratio ~ 2.58)
            bool isFarOuter = (ratio >= 10.0 || (rArr > rDep * 8.0)); // 如天王星 (19.2 AU), 海王星 (30.0 AU)
            bool isMidOuter = (ratio >= 4.0 && !isFarOuter); // 如木星 (5.2 AU), 土星 (9.58 AU)

            var hp = new TopologyHyperparameters { OrbitRadiusRatio = ratio };

            if (isDeepInner)
            {
                // 极低势阱内行星: 需要巨大减速借力 (单次借力无法抹平巨大速度差)
                hp.MaxSwingBys = 6;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 2;
                hp.MaxBackSpacing = 3;
                hp.Rationale = $"深重力势阱内潜行星任务 (半长轴比={ratio:F2})，配置多重连续同星共振调相与减速拓扑 (无上限)";
            }
            else if (isFarOuter)
            {
                // 深空远外行星: 能量跨度巨大，直达机动不可行，必须借助巨行星超大引力弹弓
                hp.MaxSwingBys = 6;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 2;
                hp.MaxBackSpacing = 3;
                hp.Rationale = $"远外深空大势能跨度行星任务 (半长轴比={ratio:F2})，配置多重同星共振蓄能与巨行星引力跳板拓扑 (无上限)";
            }
            else if (isMidOuter)
            {
                // 中远外行星: 借助中途行星加速
                hp.MaxSwingBys = 4;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 1;
                hp.MaxBackSpacing = 2;
                hp.Rationale = $"中远大势能跨度行星任务 (半长轴比={ratio:F2})，配置引力助推增能链条 (无上限)";
            }
            else
            {
                // 近邻行星: 直达霍曼为基准，辅以单次辅助借力
                hp.MaxSwingBys = 3;
                hp.MaxResonances = int.MaxValue; // 用户要求：不设人为上限
                hp.MaxBackLegs = 1;
                hp.MaxBackSpacing = 1;
                hp.Rationale = $"近邻行星任务 (半长轴比={ratio:F2})，以直达霍曼转移为主，辅以单次高价值借力测试 (无上限)";
            }

            return hp;
        }

        /// <summary>
        /// 纯基于天体物理常数 (SMA, Mu, Radius) 的普适天体力学拓扑生成算法 (0 硬编码天体名称，对任意恒星系通用)
        /// </summary>
        public static List<List<string>> GenerateOptimalFlybySequences(
            MGAAdaptiveSolverConfig config, 
            int maxChains = 8)
        {
            if (config != null && config.MaxCandidateTopologies > 0)
            {
                maxChains = config.MaxCandidateTopologies;
            }
            var sequences = new List<List<string>>();
            if (string.IsNullOrEmpty(config.DeparturePlanet) || string.IsNullOrEmpty(config.ArrivalPlanet))
                return sequences;

            // 1. 若用户未指定中途借力意向天体，直达序列作为最高优先级基准
            if (config.FlybySequence == null || config.FlybySequence.Count == 0)
            {
                sequences.Add(new List<string> { config.ArrivalPlanet });
            }

            // 若用户指定了特定借力链，按配置自动展开乱序与高价值插序
            if (config.FlybySequence != null && config.FlybySequence.Count > 0)
            {
                if (config.EnablePermutationsAndInsertions)
                {
                    var expanded = ExpandPermutationsAndInsertions(config.DeparturePlanet, config.ArrivalPlanet, config.FlybySequence, maxChains * 2);
                    foreach (var chain in expanded)
                    {
                        if (!sequences.Any(s => s.SequenceEqual(chain)))
                        {
                            sequences.Add(chain);
                        }
                    }
                }
                else
                {
                    if (!sequences.Any(s => s.SequenceEqual(config.FlybySequence)))
                    {
                        sequences.Insert(0, new List<string>(config.FlybySequence));
                    }
                }
            }
            else
            {
                // 2. 获取始发天体与目标天体物理属性
                var pDep = SolarSystemCatalog.GetPlanet(config.DeparturePlanet);
                var pArr = SolarSystemCatalog.GetPlanet(config.ArrivalPlanet);
                if (pDep != null && pArr != null)
                {
                    double rDep = (pDep.SMA > 0) ? pDep.SMA : 1.496e8;
                    double rArr = (pArr.SMA > 0) ? pArr.SMA : 1.496e8;
                    string parentCentral = pDep.ParentName ?? SolarSystemCatalog.RootStarName;

                    // 3. 提取同一中心天体系统下的所有可用行星，排除中心恒星、始发与终点，并按轨道半长轴升序排列
                    string rootStar = SolarSystemCatalog.RootStarName;
                    string central = config.CentralBody ?? parentCentral;
                    var catalog = SolarSystemCatalog.Planets;
                    var sisterBodies = catalog.Values
                        .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                    !b.Name.Equals(parentCentral, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(central, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(config.DeparturePlanet, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(config.ArrivalPlanet, StringComparison.OrdinalIgnoreCase) &&
                                    (b.ParentName == null || b.ParentName.Equals(parentCentral, StringComparison.OrdinalIgnoreCase) || b.ParentName.Equals(rootStar, StringComparison.OrdinalIgnoreCase)) &&
                                    b.SMA > 0 && b.Mu > 0 && b.RadiusKm > 0)
                        .OrderBy(b => b.SMA)
                        .ToList();

                    // 辅助函数: 天体引力弯曲效能指数 (Mu / Radius)
                    double FlybyPower(SolarPlanet p) => (p != null && p.RadiusKm > 0) ? (p.Mu / p.RadiusKm) : 0.0;

                    // 4. 场景 A: 深入母星引力井任务 (rArr < rDep)
                    if (rArr < rDep)
                    {
                        // 寻找位于始发与目标之间的所有中间天体 (按引力效能降序)
                        var intermediateBodies = sisterBodies
                            .Where(b => b.SMA > rArr && b.SMA < rDep)
                            .OrderByDescending(b => FlybyPower(b))
                            .ToList();

                        // 取引力助推能力最强的中间天体作为主减速中继跳板
                        var bridge = intermediateBodies.FirstOrDefault();
                        if (bridge != null)
                        {
                            // 4.1 单次中继减速: dep -> bridge -> arr
                            sequences.Add(new List<string> { bridge.Name, config.ArrivalPlanet });

                            // 4.2 若目标属于极深势阱 (rArr < rDep * 0.5): 自动派生多重共振与中继衰减拓扑
                            if (rArr < rDep * 0.5)
                            {
                                // dep -> bridge -> bridge -> arr
                                sequences.Add(new List<string> { bridge.Name, bridge.Name, config.ArrivalPlanet });
                                // dep -> bridge -> bridge -> arr -> arr (目标本星共振)
                                sequences.Add(new List<string> { bridge.Name, bridge.Name, config.ArrivalPlanet, config.ArrivalPlanet });
                                // dep -> bridge -> bridge -> arr -> arr -> arr (多重深引力井本星衰减)
                                sequences.Add(new List<string> { bridge.Name, bridge.Name, config.ArrivalPlanet, config.ArrivalPlanet, config.ArrivalPlanet });
                                // dep -> bridge -> dep -> arr (内-外回折弹弓)
                                sequences.Add(new List<string> { bridge.Name, config.DeparturePlanet, config.ArrivalPlanet });
                            }
                        }
                    }
                    // 5. 场景 B: 远外深空跨越任务 (rArr > rDep)
                    else
                    {
                        // 5.1 寻找位于始发与目标之间的外侧天体 (按引力效能降序)
                        var intermediateOuters = sisterBodies
                            .Where(b => b.SMA > rDep && b.SMA < rArr)
                            .OrderByDescending(b => FlybyPower(b))
                            .ToList();

                        // 5.2 寻找始发天体内侧的最强引力天体 (用于内潜反弹加速 Inward Assist)
                        var innerAssist = sisterBodies
                            .Where(b => b.SMA < rDep)
                            .OrderByDescending(b => FlybyPower(b))
                            .FirstOrDefault();

                        // 巨行星跳板: 中途引力最大的天体
                        var primaryGiant = intermediateOuters.FirstOrDefault();
                        // 次级巨行星或外侧大行星
                        var secondaryGiant = intermediateOuters.Skip(1).FirstOrDefault();

                        if (primaryGiant != null)
                        {
                            // 单次巨行星跳板: dep -> Giant -> arr
                            sequences.Add(new List<string> { primaryGiant.Name, config.ArrivalPlanet });

                            if (secondaryGiant != null)
                            {
                                // 双巨行星接力弹弓: dep -> Giant1 -> Giant2 -> arr
                                sequences.Add(new List<string> { primaryGiant.Name, secondaryGiant.Name, config.ArrivalPlanet });
                            }

                            // 全系巨行星大巡游接力 (Grand Tour / Voyager 2): 沿半长轴单调递增将所有中途巨行星全链串联
                            var allOuterGiants = intermediateOuters
                                .Where(b => b.Mu >= 5.0e6 || b.RadiusKm >= 20000.0)
                                .OrderBy(b => b.SMA)
                                .ToList();
                            if (allOuterGiants.Count >= 3)
                            {
                                var grandTour = allOuterGiants.Select(g => g.Name).ToList();
                                grandTour.Add(config.ArrivalPlanet);
                                sequences.Add(grandTour);
                            }

                            // 始发天体自身 2:1 EGA 共振蓄能 (如 Juno 2011): dep -> dep -> Giant -> arr
                            sequences.Add(new List<string> { config.DeparturePlanet, primaryGiant.Name, config.ArrivalPlanet });

                            // 中继渐进跳板: 若始发天体与主巨行星之间存在中等行星
                            var steppingStone = sisterBodies
                                .Where(b => b.SMA > rDep && b.SMA < primaryGiant.SMA)
                                .OrderByDescending(b => FlybyPower(b))
                                .FirstOrDefault();

                            if (steppingStone != null)
                            {
                                sequences.Add(new List<string> { steppingStone.Name, primaryGiant.Name, config.ArrivalPlanet });
                            }

                            // 连环多阶引力跳跃: dep -> steppingStone -> Giant1 -> Giant2 -> arr (人类未曾实践的连环阶跃拓扑)
                            if (steppingStone != null && secondaryGiant != null)
                            {
                                sequences.Add(new List<string> { steppingStone.Name, primaryGiant.Name, secondaryGiant.Name, config.ArrivalPlanet });
                            }

                            // 巨行星自身同星共振泵升: dep -> Giant -> Giant -> arr
                            sequences.Add(new List<string> { primaryGiant.Name, primaryGiant.Name, config.ArrivalPlanet });

                            // 始发天体交替回折蓄能超级弹弓: dep -> steppingStone -> dep -> Giant -> arr (Rosetta 级超级泵升)
                            if (steppingStone != null)
                            {
                                sequences.Add(new List<string> { steppingStone.Name, config.DeparturePlanet, primaryGiant.Name, config.ArrivalPlanet });
                            }

                            // 复合巨行星接力内潜反弹: dep -> Inner -> Giant1 -> Giant2 -> arr
                            if (innerAssist != null && secondaryGiant != null)
                            {
                                sequences.Add(new List<string> { innerAssist.Name, primaryGiant.Name, secondaryGiant.Name, config.ArrivalPlanet });
                            }
                        }

                        // 5.3 普适内潜反弹蓄能跳板 (Inward Swing-by Assist Ladders):
                        // 当目标位于外侧 (rArr > rDep) 且母星系内侧存在强引力天体 (如金星) 时，
                        // 先潜入内侧借力，再折返出发天体泵能甩向外侧
                        if (innerAssist != null && (rArr >= rDep * 2.5))
                        {
                            // 基础内潜折返: dep -> Inner -> dep -> arr (VEGA)
                            sequences.Add(new List<string> { innerAssist.Name, config.DeparturePlanet, config.ArrivalPlanet });
                            // 伽利略级双地回折泵能: dep -> Inner -> dep -> dep -> arr (VEEGA / Galileo 拓扑！)
                            sequences.Add(new List<string> { innerAssist.Name, config.DeparturePlanet, config.DeparturePlanet, config.ArrivalPlanet });
                            // 双金星内潜共振回折: dep -> Inner -> Inner -> dep -> arr (VVEGA)
                            sequences.Add(new List<string> { innerAssist.Name, innerAssist.Name, config.DeparturePlanet, config.ArrivalPlanet });

                            // 若中途存在显著外侧中继巨行星 (如去土星/海王星途经木星):
                            if (primaryGiant != null && (primaryGiant.Mu >= 5.0e6 || primaryGiant.RadiusKm >= 20000.0) && primaryGiant.SMA < rArr * 0.8)
                            {
                                // 卡西尼级复合多星跳板: dep -> Inner -> Inner -> dep -> Giant -> arr (VVEJGA / Cassini 拓扑！)
                                sequences.Add(new List<string> { innerAssist.Name, innerAssist.Name, config.DeparturePlanet, primaryGiant.Name, config.ArrivalPlanet });
                                // 伽利略+外巨行星中继: dep -> Inner -> dep -> dep -> Giant -> arr
                                sequences.Add(new List<string> { innerAssist.Name, config.DeparturePlanet, config.DeparturePlanet, primaryGiant.Name, config.ArrivalPlanet });
                            }
                        }
                        else if (innerAssist != null)
                        {
                            // 无中间巨行星时，采用内潜反弹直接飞往目标
                            sequences.Add(new List<string> { innerAssist.Name, config.ArrivalPlanet });
                        }
                    }
                }
            }

            // 6. 若启用星历与物理可达性前置过滤，执行开普勒 Vis-Viva 能量赤字审计与不合理序列剪枝
            if (config.EnableEphemerisReachabilityFilter)
            {
                double budgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : double.MaxValue;
                double dsmBudgetMs = (config.MaxDsmDeltaVMs > 0) ? config.MaxDsmDeltaVMs : budgetMs;
                var filtered = MGAEphemerisReachabilityAnalyzer.FilterAndPrioritizeReachableSequences(
                    config.DeparturePlanet, config.ArrivalPlanet, sequences, config.SearchStartUT, budgetMs, dsmBudgetMs, config.FlybySequence);
                if (filtered != null && filtered.Count > 0)
                {
                    sequences = filtered.Select(f => f.FlybyChain).ToList();
                }
            }

            return sequences.Take(maxChains).ToList();
        }

        /// <summary>
        /// 基于组合数学与天体动力学能量梯度的序列乱序排列 (Permutations) 与高价值中继插序 (Interleaving)
        /// </summary>
        public static List<List<string>> ExpandPermutationsAndInsertions(
            string departure, 
            string arrival, 
            List<string> rawFlybys, 
            int maxCombinations = 8)
        {
            var result = new List<List<string>>();
            if (rawFlybys == null || rawFlybys.Count == 0) return result;

            var cleaned = new List<string>(rawFlybys.Where(p => !string.IsNullOrEmpty(p)));
            if (cleaned.Count > 0 && cleaned[0].Equals(departure, StringComparison.OrdinalIgnoreCase))
                cleaned.RemoveAt(0);
            if (cleaned.Count > 0 && cleaned[cleaned.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                cleaned.RemoveAt(cleaned.Count - 1);
            if (cleaned.Count == 0) return result;

            // 1. 用户原始序列作为最高优先级基准
            result.Add(new List<string>(cleaned));

            // 2. 中途天体乱序全排列探索 (Permutations)
            // 物理约束 (IJCAI 单调能量梯级): 交换中间天体顺序可能把外行星排到内爬升体之前，产生非物理跳跃
            // (如 Cassini 的 Earth➔Jupiter➔Venus)，导致发射能量天文级虚高。仅保留"折叠共振重复后 SMA 单调"的排列
            if (cleaned.Count >= 2 && cleaned.Count <= 5)
            {
                var allPerms = GetPermutations(cleaned);
                foreach (var p in allPerms)
                {
                    var chain = new List<string>(p);
                    if (!result.Any(r => r.SequenceEqual(chain)) && IsSmaMonotonicLadder(chain))
                    {
                        result.Add(chain);
                        if (result.Count >= maxCombinations) break;
                    }
                }
            }

            // 3. 高价值中继引力弹弓插序探索 (Inward Swing-by Assist Insertion)
            // 普适物理律: 前往外侧高轨天体 (rArr > rDep) 时，若母星系内侧存在强引力天体，
            // 先向内潜入该天体借力反弹，可显著削减发射能量
            var pDepObj = SolarSystemCatalog.GetPlanet(departure);
            var pArrObj = SolarSystemCatalog.GetPlanet(arrival);
            if (pDepObj != null && pArrObj != null && pArrObj.SMA > pDepObj.SMA)
            {
                // 寻找位于出发天体内侧的最强引力中继天体 (Mu / Radius 极大值)
                string parentSys = pDepObj.ParentName ?? SolarSystemCatalog.RootStarName;
                string rootStar = SolarSystemCatalog.RootStarName;
                var innerAssist = SolarSystemCatalog.Planets.Values
                    .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                !b.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) &&
                                !b.Name.Equals(parentSys, StringComparison.OrdinalIgnoreCase) &&
                                !b.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase) &&
                                (b.ParentName == null || b.ParentName.Equals(parentSys, StringComparison.OrdinalIgnoreCase) || b.ParentName.Equals(rootStar, StringComparison.OrdinalIgnoreCase)) &&
                                b.SMA > 0 && b.SMA < pDepObj.SMA && b.Mu > 0 && b.RadiusKm > 0)
                    .OrderByDescending(b => b.Mu / b.RadiusKm)
                    .FirstOrDefault();

                if (innerAssist != null)
                {
                    var candidatesToInsert = new List<List<string>>();
                    foreach (var baseChain in result.Take(4).ToList())
                    {
                        if (!baseChain[0].Equals(innerAssist.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            var inserted = new List<string> { innerAssist.Name };
                            inserted.AddRange(baseChain);
                            if (!result.Any(r => r.SequenceEqual(inserted)))
                            {
                                candidatesToInsert.Add(inserted);
                            }

                            // 双重内潜调相 (如 Venus -> Venus，对齐 BepiColombo / MESSENGER 权威构型)
                            var insertedDouble = new List<string> { innerAssist.Name, innerAssist.Name };
                            insertedDouble.AddRange(baseChain);
                            if (!result.Any(r => r.SequenceEqual(insertedDouble)))
                            {
                                candidatesToInsert.Add(insertedDouble);
                            }
                        }
                    }

                    // 3.3 MESSENGER 式出发星前置减速链 (Deep-Well Pre-Deceleration Chain)
                    // 真实物理法则: NASA MESSENGER 进入水星前先飞掠地球 1 次 + 金星 2 次逐级减速，
                    // 把到达水星的 V∞ 从 ~10 km/s 压到 ~5.5 km/s，再以水星 3 次飞掠逐级减速泊入；
                    // ESA BepiColombo 同构 (Earth x1 + Venus x2 + Mercury x6)。
                    // 当前求解器此前只生成 [inner]+chain / [inner,inner]+chain (以出发星为隐含起点)，
                    // 缺 "dep->dep" 出发星自身前置飞掠段，导致 Scenario 9 首腿直接掉深井 (V→M 单腿 4233 m/s 硬推)。
                    // 此处显式派生 MESSENGER/BepiColombo 权威前置构型，把首段减速责任分摊到地球/金星逐级飞掠。
                    bool deepWellTarget = pArrObj != null && pDepObj != null &&
                                          pArrObj.SMA < pDepObj.SMA * 0.5;
                    if (!deepWellTarget && pArrObj != null && pDepObj != null)
                    {
                        // 若目标是远外天体 (如 Neptune)，同样允许出发星前置飞掠来压低发射能量
                        deepWellTarget = pArrObj.SMA >= pDepObj.SMA * MGADefaults.GiantAssistDistanceRatio;
                    }
                    if (deepWellTarget && cleaned.Count > 0)
                    {
                        // 变体 A (完整 MESSENGER 式): dep -> dep -> inner -> inner -> 意图...
                        var messengerFull = new List<string> { departure, innerAssist.Name, innerAssist.Name };
                        messengerFull.AddRange(cleaned);
                        if (!result.Any(r => r.SequenceEqual(messengerFull)))
                        {
                            candidatesToInsert.Add(messengerFull);
                        }
                        // 变体 B (单次出发星前置 + 双内潜): dep -> dep -> inner -> 意图...
                        var messengerSingle = new List<string> { departure, innerAssist.Name };
                        messengerSingle.AddRange(cleaned);
                        if (!result.Any(r => r.SequenceEqual(messengerSingle)))
                        {
                            candidatesToInsert.Add(messengerSingle);
                        }
                        // 变体 C (出发星前置 + 目标自身共振收尾): dep -> dep -> inner -> inner -> 意图 -> 意图
                        var messengerResonant = new List<string> { departure, innerAssist.Name, innerAssist.Name };
                        messengerResonant.AddRange(cleaned);
                        if (cleaned.Count > 0)
                        {
                            // 总量守衡: 收尾追加仅允许在目标自减速飞掠总数 <4 时进行,
                            // 杜绝 x5/x6 超长共振链 (DE 维度 25+) 霸占演化池。
                            string tail = cleaned[cleaned.Count - 1];
                            int tailCount = 0;
                            foreach (var nm in messengerResonant)
                            {
                                if (nm.Equals(tail, StringComparison.OrdinalIgnoreCase)) tailCount++;
                            }
                            if (tailCount < 4) messengerResonant.Add(tail);
                        }
                        if (!result.Any(r => r.SequenceEqual(messengerResonant)))
                        {
                            candidatesToInsert.Add(messengerResonant);
                        }
                        // 变体 D/E (出发星前置 + 深势阱目标自身多次飞掠自减速): 意图 x3 / x4
                        // "水星自减速"真实物理: MESSENGER 以水星 3 次飞掠逐级减速泊入, BepiColombo 以 6 次;
                        // 显式派生中等长度自减速链 (总 6-7 腿, DE 维度 13-15), 避免仅 1 次水星飞掠
                        // 迫使 V→M 单腿大 DSM 硬推 (此前 2659 m/s 超标根因)。
                        if (cleaned.Count > 0)
                        {
                            string selfBody = cleaned[cleaned.Count - 1];
                            var selfBodyObj = SolarSystemCatalog.GetPlanet(selfBody);
                            if (selfBodyObj != null && selfBodyObj.SMA < pDepObj.SMA * 0.45)
                            {
                                // 总量守衡: 变体 D/E 只把总自减速飞掠次数"补足"到 3~4 次 (MESSENGER x3 / BepiColombo 级 x4),
                                // 若 cleaned 已含 x3/x4 共振链则不再追加, 严禁制造 x5/x6 超长链 (DE 维度 25+ 永不收敛)。
                                int existingSelf = 0;
                                foreach (var nm in cleaned)
                                {
                                    if (nm.Equals(selfBody, StringComparison.OrdinalIgnoreCase)) existingSelf++;
                                }
                                foreach (int targetSelf in new int[] { 3, 4 })
                                {
                                    int need = targetSelf - existingSelf;
                                    if (need <= 0) continue;
                                    var messengerSelfDecel = new List<string> { departure, innerAssist.Name, innerAssist.Name };
                                    messengerSelfDecel.AddRange(cleaned);
                                    for (int s = 0; s < need; s++)
                                    {
                                        messengerSelfDecel.Add(selfBody);
                                    }
                                    if (!result.Any(r => r.SequenceEqual(messengerSelfDecel)))
                                    {
                                        candidatesToInsert.Add(messengerSelfDecel);
                                    }
                                }
                            }
                        }
                    }

                    foreach (var ins in candidatesToInsert)
                    {
                        if (result.Count < maxCombinations + 4)
                        {
                            result.Add(ins);
                        }
                    }
                }
            }

            // 3.5 远外深空巨行星外向弹弓插序探索 (Outward Giant Swing-by Assist Insertion)
            // 普适物理律: 前往远外深空天体 (rArr >= rDep * GiantAssistDistanceRatio) 时，
            // 若尚未经过中间巨行星 (如木星)，在到达目标前插入位于中途的极大引力天体 (Mu 极大值)，
            // 是物理上实现深空大角度抛射与零工质加速的根本保证
            if (pArrObj != null && pDepObj != null && pArrObj.SMA >= pDepObj.SMA * MGADefaults.GiantAssistDistanceRatio)
            {
                string parentSys = pDepObj.ParentName ?? SolarSystemCatalog.RootStarName;
                string rootStar = SolarSystemCatalog.RootStarName;
                var giantAssist = SolarSystemCatalog.Planets.Values
                    .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                !b.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) &&
                                !b.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase) &&
                                !b.Name.Equals(parentSys, StringComparison.OrdinalIgnoreCase) &&
                                !b.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase) &&
                                (b.ParentName == null || b.ParentName.Equals(parentSys, StringComparison.OrdinalIgnoreCase) || b.ParentName.Equals(rootStar, StringComparison.OrdinalIgnoreCase)) &&
                                b.SMA > pDepObj.SMA && b.SMA < pArrObj.SMA && b.Mu > 0 && b.RadiusKm > 0)
                    .OrderByDescending(b => b.Mu)
                    .FirstOrDefault();

                if (giantAssist != null)
                {
                    var candidatesToInsertGiant = new List<List<string>>();
                    foreach (var baseChain in result.Take(6).ToList())
                    {
                        if (!baseChain.Any(p => p.Equals(giantAssist.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            var inserted = new List<string>(baseChain);
                            inserted.Add(giantAssist.Name);
                            if (!result.Any(r => r.SequenceEqual(inserted)))
                            {
                                candidatesToInsertGiant.Add(inserted);
                            }
                        }
                    }
                    foreach (var ins in candidatesToInsertGiant)
                    {
                        if (result.Count < maxCombinations + 8)
                        {
                            result.Add(ins);
                        }
                    }
                }
            }

            // 3.8 势阱爬升中继插序 (Inward-to-Outward Gravity Well Climbing Assist)
            // 真实物理法则：当借力链从太阳深引力井天体跨越至远外行星时，二体能量极差大，
            // 必须插入中途爬升天体逐级泵升远心点，杜绝超大 DSM 消耗！
            if (pDepObj != null)
            {
                var climbingCandidates = new List<List<string>>();
                foreach (var baseChain in result.Take(8).ToList())
                {
                    var chainWithArr = new List<string>(baseChain);
                    chainWithArr.Add(arrival);

                    for (int i = 0; i < chainWithArr.Count - 1; i++)
                    {
                        var pCurr = SolarSystemCatalog.GetPlanet(chainWithArr[i]);
                        var pNext = SolarSystemCatalog.GetPlanet(chainWithArr[i + 1]);
                        if (pCurr != null && pNext != null && pCurr.SMA > 0 && pNext.SMA > 0)
                        {
                            if (pCurr.SMA < pDepObj.SMA && pNext.SMA >= pDepObj.SMA * MGADefaults.GravityClimbingDistanceRatio)
                            {
                                // 获取按半长轴严格升序排列的所有中间跳板天体 (严格排除恒星根天体与卫星，仅保留母恒星直接绕行行星)
                                string parentSys = pDepObj.ParentName ?? SolarSystemCatalog.RootStarName;
                                string rootStar = SolarSystemCatalog.RootStarName;
                                var steppingStones = SolarSystemCatalog.Planets.Values
                                    .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                                !b.IsMoon &&
                                                !b.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals(parentSys, StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals(rootStar, StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals("Sun", StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals("Sol", StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase) &&
                                                (b.ParentName == null || b.ParentName.Equals(parentSys, StringComparison.OrdinalIgnoreCase) || b.ParentName.Equals(rootStar, StringComparison.OrdinalIgnoreCase)) &&
                                                b.SMA > pCurr.SMA && b.SMA < pNext.SMA && b.Mu >= MGADefaults.MinSteppingStoneMu && b.RadiusKm > 0 &&
                                                !b.Name.Equals(chainWithArr[i], StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals(chainWithArr[i + 1], StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(b => b.SMA)
                                    .ToList();

                                // 筛选具显著引力助推能力的行星跳板 (内行星保留势深显著天体，外行星选取巨行星)
                                var innerLadder = steppingStones.Where(b => b.SMA < MGADefaults.InnerSystemMaxSmaKm && 
                                    ((b.GravityPotentialDepth >= MGADefaults.MinAssistPowerIndexInner) || b.Mu >= MGADefaults.GiantPlanetMinMu)).ToList();
                                var outerGiants = steppingStones.Where(b => b.SMA >= MGADefaults.InnerSystemMaxSmaKm).OrderBy(b => b.SMA).Take(2).ToList();
                                var ladder = new List<SolarPlanet>(innerLadder);
                                if (outerGiants.Count > 0) ladder.Add(outerGiants[0]);

                                if (ladder.Count > 0)
                                {
                                    // 1. 严格按半长轴升序逐阶爬升梯子: [pCurr] ➔ Venus ➔ Earth ➔ Jupiter
                                    var ladderChain = new List<string>(baseChain);
                                    ladderChain.InsertRange(i + 1, ladder.Select(l => l.Name));
                                    if (!result.Any(r => r.SequenceEqual(ladderChain)) && !climbingCandidates.Any(r => r.SequenceEqual(ladderChain)))
                                    {
                                        climbingCandidates.Add(ladderChain);
                                    }

                                    // 2. 出发星 2:1 共振槽 (如 Earth -> Earth 2:1 调相蓄能)
                                    var depBodyInLadder = ladder.FirstOrDefault(l => l.Name.Equals(pDepObj.Name, StringComparison.OrdinalIgnoreCase));
                                    if (depBodyInLadder != null)
                                    {
                                        var resonantLadder = new List<string>(baseChain);
                                        var ladderWithRes = new List<string>();
                                        foreach (var l in ladder)
                                        {
                                            ladderWithRes.Add(l.Name);
                                            if (l.Name.Equals(depBodyInLadder.Name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                ladderWithRes.Add(depBodyInLadder.Name); // 插入同星 2:1 开普勒共振调相
                                            }
                                        }
                                        resonantLadder.InsertRange(i + 1, ladderWithRes);
                                        if (!result.Any(r => r.SequenceEqual(resonantLadder)) && !climbingCandidates.Any(r => r.SequenceEqual(resonantLadder)))
                                        {
                                            climbingCandidates.Add(resonantLadder);
                                        }

                                        // 3. 内侧高引力跳板回升多次共振 + 出发星 2:1 共振 (双重多阶共振)
                                        var dualResLadder = new List<string>(baseChain);
                                        var ladderWithDualRes = new List<string>();
                                        var innerHighGravityBody = ladder.Where(l => l.SMA < pDepObj.SMA).OrderByDescending(l => l.GravityPotentialDepth).FirstOrDefault();
                                        foreach (var l in ladder)
                                        {
                                            ladderWithDualRes.Add(l.Name);
                                            if ((innerHighGravityBody != null && l.Name.Equals(innerHighGravityBody.Name, StringComparison.OrdinalIgnoreCase)) || 
                                                l.Name.Equals(depBodyInLadder.Name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                ladderWithDualRes.Add(l.Name);
                                            }
                                        }
                                        dualResLadder.InsertRange(i + 1, ladderWithDualRes);
                                        if (!result.Any(r => r.SequenceEqual(dualResLadder)) && !climbingCandidates.Any(r => r.SequenceEqual(dualResLadder)))
                                        {
                                            climbingCandidates.Add(dualResLadder);
                                        }
                                    }

                                    // 4. 双巨行星超级弹弓 (木星 + 土星)
                                    if (outerGiants.Count >= 2)
                                    {
                                        var saturnLadder = new List<string>(baseChain);
                                        var ladderSat = new List<string>(innerLadder.Select(l => l.Name));
                                        ladderSat.Add(outerGiants[0].Name);
                                        ladderSat.Add(outerGiants[1].Name);
                                        saturnLadder.InsertRange(i + 1, ladderSat);
                                        if (!result.Any(r => r.SequenceEqual(saturnLadder)) && !climbingCandidates.Any(r => r.SequenceEqual(saturnLadder)))
                                        {
                                            climbingCandidates.Add(saturnLadder);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var cc in climbingCandidates)
                {
                    if (result.Count < maxCombinations + 48)
                    {
                        result.Add(cc);
                    }
                }
            }

            // 4. 同星多次共振借力拓扑扩展 (Multi-Resonant Flyby Expansion) - 用户明确要求：不设人为上限
            // 普适物理律: 任何借力链条中的中间行星，若由于自身质量较小或相对几何限制导致单次转向角有限，
            // 均可通过 1:1 或 2:1 轨道周期共振实施多次借力。
            var resonantChains = new List<List<string>>();
            foreach (var baseChain in result.Take(32).ToList())
            {
                for (int i = 0; i < baseChain.Count; i++)
                {
                    string body = baseChain[i];
                    var pBody = SolarSystemCatalog.GetPlanet(body);
                    if (pBody != null && pBody.Mu > 0 && pBody.RadiusKm > 0)
                    {
                        double flybyPower = pBody.GravityPotentialDepth;
                        int resMax = (flybyPower < MGADefaults.FlybyPowerWeakThreshold) ? 6 : (flybyPower < MGADefaults.FlybyPowerMediumThreshold ? 4 : 2);
                        for (int n = 1; n <= resMax; n++)
                        {
                            var resChainN = new List<string>(baseChain);
                            for (int k = 0; k < n; k++)
                            {
                                resChainN.Insert(i, body);
                            }
                            if (!result.Any(r => r.SequenceEqual(resChainN)) && !resonantChains.Any(r => r.SequenceEqual(resChainN)))
                            {
                                resonantChains.Add(resChainN);
                            }
                        }
                    }
                }
            }
            foreach (var rc in resonantChains)
            {
                result.Add(rc);
            }

            // 5. 交替回折近拱点提升与能量泵升拓扑 (Alternating Body Pumping: D-A-D-A / PE-Raising)
            // 普适天体力学原理: 
            // 探测器从始发天体 D 出发到第一借力天体 A (D->A) 抬升远拱点，再回折飞掠始发天体 D (A->D) 抬升近拱点 (PE-Raising) 与总机械能
            // 随后再次飞掠 A (D->A) 获得二次暴增动能，顺势切入主带甚至外层目标，将出射能量与中途机动压至极限！
            var pumpingChains = new List<List<string>>();
            foreach (var baseChain in result.Take(4).ToList())
            {
                string firstBody = baseChain[0];
                if (!firstBody.Equals(departure, StringComparison.OrdinalIgnoreCase))
                {
                    // 衍生 D -> firstBody -> D -> [Remaining] (近拱点抬升 PE-Raising)
                    var dadChain = new List<string> { firstBody, departure };
                    dadChain.AddRange(baseChain.Skip(1));
                    if (!result.Any(r => r.SequenceEqual(dadChain)) && !pumpingChains.Any(r => r.SequenceEqual(dadChain)))
                    {
                        pumpingChains.Add(dadChain);
                    }

                    // 衍生 D -> firstBody -> D -> firstBody -> [Remaining] (双交替超级泵升)
                    var dadaChain = new List<string> { firstBody, departure, firstBody };
                    dadaChain.AddRange(baseChain.Skip(1));
                    if (!result.Any(r => r.SequenceEqual(dadaChain)) && !pumpingChains.Any(r => r.SequenceEqual(dadaChain)))
                    {
                        pumpingChains.Add(dadaChain);
                    }
                }
            }
            foreach (var pc in pumpingChains)
            {
                result.Add(pc);
            }

            // 6. 交错式双星多重共振中继泵升拓扑 (Interleaved Resonant Bridge: A^n -> B -> A^m)
            // 用户核心洞察：第 n 次单星 A 共振后，轨道高度刚好经过某星球 B，便可利用 B 施加大偏转角与能量跳变，
            // 随后落回 A 继续展开第 m 次共振，形成 A...A -> B -> A...A 的多段交错共振序列！
            var interleavedChains = new List<List<string>>();
            foreach (var baseChain in result.Take(6).ToList())
            {
                for (int i = 0; i < baseChain.Count; i++)
                {
                    string bodyA = baseChain[i];
                    var pA = SolarSystemCatalog.GetPlanet(bodyA);
                    if (pA == null || pA.SMA <= 0) continue;

                    // 寻找轨道相邻或强引力势阱中继中介天体 B (如 Mercury 的中介是 Venus; Venus 的中介是 Earth; Earth 的中介是 Venus 或 Jupiter)
                    string parentSysN = pA.ParentName ?? SolarSystemCatalog.RootStarName;
                    string rootStarN = SolarSystemCatalog.RootStarName;
                    var neighborBList = SolarSystemCatalog.Planets.Values
                        .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                    !b.IsMoon &&
                                    !b.Name.Equals(bodyA, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(arrival, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(parentSysN, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(rootStarN, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals("Sun", StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals("Sol", StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals("Kerbol", StringComparison.OrdinalIgnoreCase) &&
                                    b.Mu > 0 && b.RadiusKm > 0 &&
                                    b.SMA > 0 && Math.Abs(Math.Log(b.SMA / pA.SMA)) < MGADefaults.NeighborBodyMaxLogSmaRatio)
                        .OrderByDescending(b => b.GravityPotentialDepth)
                        .Take(2)
                        .ToList();

                    foreach (var pB in neighborBList)
                    {
                        string bodyB = pB.Name;
                        // 衍生 A^n -> B -> A^m (例如 AAA -> B -> AAA)
                        for (int n = 1; n <= 3; n++)
                        {
                            for (int m = 1; m <= 3; m++)
                            {
                                var chain = new List<string>();
                                for (int k = 0; k < i; k++) chain.Add(baseChain[k]);
                                for (int k = 0; k < n; k++) chain.Add(bodyA);
                                chain.Add(bodyB);
                                for (int k = 0; k < m; k++) chain.Add(bodyA);
                                for (int k = i + 1; k < baseChain.Count; k++) chain.Add(baseChain[k]);

                                if (!result.Any(r => r.SequenceEqual(chain)) && !interleavedChains.Any(r => r.SequenceEqual(chain)))
                                {
                                    interleavedChains.Add(chain);
                                }
                            }
                        }
                    }
                }
            }
            foreach (var ic in interleavedChains)
            {
                result.Add(ic);
            }

            // 严格杜绝末尾重复追加 Arrival (如 Neptune -> Neptune) 或开头残留 Departure
            foreach (var chain in result)
            {
                while (chain.Count > 0 && chain[chain.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                {
                    chain.RemoveAt(chain.Count - 1);
                }
                while (chain.Count > 0 && chain[0].Equals(departure, StringComparison.OrdinalIgnoreCase))
                {
                    chain.RemoveAt(0);
                }
            }
            result = result.Where(c => c.Count > 0).ToList();

            return result;
        }

        /// <summary>
        /// 全局通用双圆霍曼转移基准能量计算 (100% 优先委托 PykepBridge.CalcHohmannTransfer 原生 C++ 动力学)
        /// 理论物理真理：任何行星际转移的理论出射与捕获下限由两体解析公式决定
        /// </summary>
        public static (double DepHohmannKms, double TotHohmannKms) CalculateUniversalHohmannBenchmark(
            string departurePlanet, 
            string arrivalPlanet, 
            double depPeKm = 200.0, 
            double arrPeKm = 200.0)
        {
            var pDep = SolarSystemCatalog.GetPlanet(departurePlanet);
            var pArr = SolarSystemCatalog.GetPlanet(arrivalPlanet);
            if (pDep == null || pArr == null || pDep.SMA <= 0 || pArr.SMA <= 0)
            {
                return (3.8, 6.5);
            }

            double muCentral = (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11);
            double r1 = pDep.SMA;
            double r2 = pArr.SMA;

            // 1. 优先调用原生 PykepBridge.CalcHohmannTransfer (100% kep3 原生)
            if (PykepBridge.CalcHohmannTransfer(r1, r2, muCentral, out _, out _, out double vInfDepNative, out double vInfArrNative)) {
                double depDvNative = PykepBridge.CalculateDepartureDeltaV(vInfDepNative, pDep, depPeKm, depPeKm);
                double arrDvNative = PykepBridge.CalculateArrivalInsertionDeltaV(vInfArrNative, pArr, arrPeKm, arrPeKm, false);
                if (!double.IsNaN(depDvNative) && !double.IsNaN(arrDvNative) && depDvNative > 0 && arrDvNative > 0) {
                    return (depDvNative, depDvNative + arrDvNative);
                }
            }

            // 2. 原生接口未就绪时的解析保底 (0 冒充原生)
            double aTrans = (r1 + r2) * 0.5;
            double vDepTrans = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r1 - 1.0 / aTrans)));
            double vDepCirc = Math.Sqrt(Math.Max(0.0, muCentral / r1));
            double vInfDep = Math.Abs(vDepTrans - vDepCirc);

            double rpDep = pDep.RadiusKm + Math.Max(10.0, depPeKm);
            double vParkDep = Math.Sqrt(Math.Max(0.0, pDep.Mu / rpDep));
            double vHypDep = Math.Sqrt(Math.Max(0.0, vInfDep * vInfDep + 2.0 * pDep.Mu / rpDep));
            double depDv = Math.Max(0.0, vHypDep - vParkDep);

            double vArrTrans = Math.Sqrt(Math.Max(0.0, muCentral * (2.0 / r2 - 1.0 / aTrans)));
            double vArrCirc = Math.Sqrt(Math.Max(0.0, muCentral / r2));
            double vInfArr = Math.Abs(vArrTrans - vArrCirc);

            double rpArr = pArr.RadiusKm + Math.Max(10.0, arrPeKm);
            double vParkArr = Math.Sqrt(Math.Max(0.0, pArr.Mu / rpArr));
            double vHypArr = Math.Sqrt(Math.Max(0.0, vInfArr * vInfArr + 2.0 * pArr.Mu / rpArr));
            double arrDv = Math.Max(0.0, vHypArr - vParkArr);

            return (depDv, depDv + arrDv);
        }

        /// <summary>
        /// 智能相位开普勒共振航时空间推导 (Auto-Derive Resonant Leg Bounds)
        /// 依据开普勒第三定律与能量守恒，为全链路中每一个航段自适应划定物理精确的 ToF 上下限网格：
        /// 1. 同星共振调相段 (P -> P):
        ///    航天器周期 T_sc = (p/q) * T_P，航时严密锁定在开普勒物理共振区间 [0.85 * T_P, 2.10 * T_P]
        /// 2. 异星转移段 (P1 -> P2):
        ///    按霍曼等效半长轴 a_trans = (a1 + a2)/2 动态划定物理转移弧段 [0.45 * T_H, 2.40 * T_H]
        /// </summary>
        public static List<(double MinDays, double MaxDays)> AutoDeriveResonantLegBounds(
            string departure, 
            string arrival, 
            List<string> flybyChain,
            string centralBody = "Sun",
            double depMinFactor = 0.65,
            double depMaxFactor = 1.50)
        {
            var fullChain = new List<string> { departure };
            if (flybyChain != null) fullChain.AddRange(flybyChain);
            if (!string.IsNullOrEmpty(arrival))
            {
                if (fullChain.Count == 0 || !fullChain[fullChain.Count - 1].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                {
                    fullChain.Add(arrival);
                }
            }

            var bounds = new List<(double MinDays, double MaxDays)>();
            double muCentralM = (SolarSystemCatalog.GetCentralMu(centralBody) > 0 
                ? SolarSystemCatalog.GetCentralMu(centralBody) 
                : (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11)) * 1e9;

            // 反梯级泵升深井识别: 全链最内天体 (如 Scenario 9 的水星 0.387 AU) 是否深于出发星 45%
            var depObjBounds = SolarSystemCatalog.GetPlanet(departure);
            double depSmaBounds = (depObjBounds != null && depObjBounds.SMA > 0) ? depObjBounds.SMA : 1.496e8;
            double deepestSma = double.MaxValue;
            foreach (var bn in fullChain)
            {
                var bp = SolarSystemCatalog.GetPlanet(bn);
                if (bp != null && bp.SMA > 0) deepestSma = Math.Min(deepestSma, bp.SMA);
            }
            bool deepWellExitMode = (deepestSma < depSmaBounds * 0.45);

            for (int k = 0; k < fullChain.Count - 1; k++)
            {
                string b1 = fullChain[k];
                string b2 = fullChain[k + 1];

                var p1 = SolarSystemCatalog.GetPlanet(b1);
                var p2 = SolarSystemCatalog.GetPlanet(b2);
                double sma1 = (p1 != null && p1.SMA > 0) ? p1.SMA : 1.496e8;
                double sma2 = (p2 != null && p2.SMA > 0) ? p2.SMA : 1.496e8;

                if (b1.Equals(b2, StringComparison.OrdinalIgnoreCase))
                {
                    // 同星共振调相段：依据天体公转周期与势阱能量包络线锁定物理有效共振槽
                    double tOrbDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(sma1 * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;

                    // 普适开普勒共振调相段：允许 1:1, 3:2, 5:2, 2:1 等物理多阶有理共振槽
                    // 1. 浅势阱小天体 (如水星/Moho μ/R < 20) 限制在 2:1 与 5:2 以内 (<= 2.6 T_orb):
                    //    2:1 (176d, 拱点抬至 0.84 AU) 是水星共振抬升主力槽, 5:2 (220d, 拱点抬至 ~1.04 AU)
                    //    是反梯级泵升的亚共振辅助槽; 更高阶 (3:1=264d) 会把航天器误抛至超大椭圆脱离内侧对齐
                    // 2. 中继大行星 (如 Earth, SMA ~ 1 AU) 若后续衔接外侧巨行星 (如 Jupiter, SMA > 5 AU):
                    //    对齐伽利略 VEEGA 机制 (2:1 共振蓄能抬升远日点至 2.16 AU, 相对速度增至 9 km/s 直冲木星):
                    //    上限锁定在 2.25 T_orb (820天)，彻底排除 3:1 (1100天) 劣质局部伪极值与超额 DSM！
                    bool hasDownstreamGiant = false;
                    for (int nxt = k + 1; nxt < fullChain.Count; nxt++)
                    {
                        var pNxt = SolarSystemCatalog.GetPlanet(fullChain[nxt]);
                        if (pNxt != null && pNxt.SMA >= sma1 * 3.5) { hasDownstreamGiant = true; break; }
                    }

                    double resMinFactor = MGADefaults.ResonanceToFMinFactor;
                    double resMaxFactor = 2.25; // 默认内侧天体 (如地球/金星) 限制在 2:1 共振以内 (0.85 ~ 2.25 T_orb)

                    if (p1 != null && p1.RadiusKm > 0 && (p1.Mu / p1.RadiusKm) < 20.0)
                    {
                        // 浅势阱小天体 (如水星/Moho) 多次共振梯级泵升 (Resonant Ladder Pump-Up):
                        // 允许开普勒多阶有理共振槽 (1:1=88d, 2:1=176d, 3:1=264d 泵升至 0.805 AU 跨越金星轨道)
                        // 统一提供 0.85 ~ 3.15 T_orb 宽裕物理搜索区间，赋能 DE 求解器自适应锁定最佳相位
                        resMinFactor = 0.85;
                        resMaxFactor = 3.15;
                    }
                    else if (p1 != null && p1.SMA < depSmaBounds * 0.85 && p1.SMA > deepestSma * 1.2)
                    {
                        resMinFactor = 0.90;
                        resMaxFactor = 2.15; // 金星等内侧中继跳板: 允许 1:1 (~225天) 与 2:1/3:2 多元开普勒共振调相
                    }
                    else if (hasDownstreamGiant && sma1 >= 1.2e8 && sma1 <= 2.2e8)
                    {
                        // 依据该共振段之后的下游走向区分前置 EGA 与后置 VEEGA:
                        string nextDest = null;
                        for (int nk = k + 1; nk < fullChain.Count; nk++)
                        {
                            if (!fullChain[nk].Equals(b1, StringComparison.OrdinalIgnoreCase))
                            {
                                nextDest = fullChain[nk];
                                break;
                            }
                        }
                        var pNextDest = !string.IsNullOrEmpty(nextDest) ? SolarSystemCatalog.GetPlanet(nextDest) : null;
                        if (pNextDest != null && pNextDest.SMA < sma1 * 0.85)
                        {
                            // 前置 1:1 EGA 调速段 (如 Earth -> Earth -> Venus，锁定 340 ~ 390 天)
                            resMinFactor = 0.93;
                            resMaxFactor = 1.07;
                        }
                        else
                        {
                            // 地球 VEEGA 2:1 共振蓄能槽 (锁定 680 ~ 780 天, 严禁向 1:1 或 3:1 漂移)
                            resMinFactor = 1.86;
                            resMaxFactor = 2.14;
                        }
                    }

                    double minLeg = Math.Max(0.5, tOrbDays * resMinFactor);
                    double maxLeg = Math.Max(minLeg + 5.0, tOrbDays * resMaxFactor);
                    bounds.Add((minLeg, maxLeg));
                }
                else
                {
                    // 异星转移段：依霍曼转移半长轴与转移几何分类动态划定物理弧段
                    double aTrans = (sma1 + sma2) * 0.5;
                    double tHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tHohmannDays <= 0 || double.IsNaN(tHohmannDays)) tHohmannDays = 200.0;

                    double ratioSma = Math.Max(sma1, sma2) / Math.Max(1.0, Math.Min(sma1, sma2));
                    double minFactor, maxFactor;

                    if (k == 0)
                    {
                        // 始发段受用户/配置设定的出射因子约束
                        minFactor = depMinFactor;
                        maxFactor = depMaxFactor;
                        // 若始发段直接连接外侧巨行星 (如 Earth -> Jupiter, SMA 跨度 >= 3.5x):
                        // 允许高速双曲切入 (0.35 ~ 1.50 T_H ~ 350d - 1500d)，覆盖 Voyager/New Horizons/Pioneer 等高能发射窗口
                        if (p2 != null && p2.SMA >= sma1 * 3.5)
                        {
                            minFactor = Math.Min(minFactor, 0.35);
                        }
                    }
                    else if (deepWellExitMode && p1 != null && p1.SMA > 0 &&
                             p1.SMA <= deepestSma * 1.05 && p2 != null && p2.SMA > p1.SMA * 1.3)
                    {
                        // 深井出口反梯级泵升腿 (Deep-Well Exit Reverse-Ladder Leg):
                        // 从全链最内天体 (水星) 爬升至外侧首个借力体 (金星):
                        // 覆盖开普勒 Type-I 单弧与金星会合窗口 (0.75 ~ 1.45 T_H ~ 79d - 153d)
                        // 杜绝因放宽至 3.50 T_H 导致航时漂移至 214d (相位偏移 180°) 产生的 3400 m/s 离面虚耗
                        minFactor = 0.75;
                        maxFactor = 1.45;
                    }
                    else if (p1 != null && p2 != null && p1.SMA < depSmaBounds * 0.85 && p2.SMA <= deepestSma * 1.05)
                    {
                        // 内潜切入深势阱腿 (如 Venus -> Mercury):
                        // 覆盖开普勒 Type-I 与 Type-II 降交点转移 (0.65 ~ 2.20 T_H ~ 48d - 165d)
                        minFactor = 0.65;
                        maxFactor = 2.20;
                    }
                    else if (sma1 >= 1.2e8 && sma1 <= 2.2e8 && p2 != null && p2.SMA >= sma1 * 3.5)
                    {
                        // 从中继大行星冲向巨行星 (如 Earth -> Jupiter):
                        // 覆盖伽利略 VEEGA 标准霍曼转移与直接切入 (0.40 ~ 1.35 T_H ~ 400d - 1350d)
                        minFactor = 0.40;
                        maxFactor = 1.35;
                    }
                    else if (p1 != null && (p1.Mu >= 5.0e6 || p1.RadiusKm >= 20000.0) && sma2 > sma1)
                    {
                        // 巨行星强引力大偏折弹弓高速双曲飞向外侧行星 (如 Jupiter -> Saturn, Saturn -> Uranus, Uranus -> Neptune):
                        // 航天器经巨行星借力后能量极大，以超高速度沿双曲弧段飞向外侧天体，
                        // 航时为标准霍曼的 0.15 ~ 0.55 T_H (覆盖 Voyager 2 木-土 2.1年、土-天 4.4年、天-海 3.6年等极速弹弓)
                        minFactor = 0.15;
                        maxFactor = 0.55;
                    }
                    else if (ratioSma <= MGADefaults.AdjacentTransferSmaRatioThreshold)
                    {
                        // 近邻天体转移 (如 Venus <-> Mercury 比值 1.87, Earth <-> Venus 比值 1.38):
                        // 物理单弧转移是以真近点角变化 delta_theta ~ 180° 为主的椭圆弧段，
                        // 航时允许 Type-I/Type-II 弹性延伸，最大放宽至 2.40 T_H
                        minFactor = MGADefaults.NearTransferToFMinFactor;
                        maxFactor = Math.Max(MGADefaults.NearTransferToFMaxFactor, 2.40);
                    }
                    else
                    {
                        // 远距离跨体系/外行星转移 (如 Jupiter -> Neptune 直达):
                        // 巨行星强引力大偏折弹弓高速双曲飞向外行星 (例如 Voyager 2 约 10 年航程，对应 0.20 ~ 0.55 T_H ~ 6.5年 - 17年)
                        // 彻底杜绝未约束至 70-85 年 void 空间导致的发散
                        bool isGiantToOuter = (p1 != null && p1.Mu >= MGADefaults.GiantPlanetMinMu && ratioSma >= MGADefaults.GiantAssistDistanceRatio);
                        minFactor = isGiantToOuter ? 0.20 : ((ratioSma >= MGADefaults.GiantAssistDistanceRatio) ? 0.08 : MGADefaults.FarTransferToFMinFactor);
                        maxFactor = isGiantToOuter ? 0.55 : MGADefaults.OuterTransferToFMaxFactor;
                    }

                    double minLeg = Math.Max(5.0, tHohmannDays * minFactor);
                    double maxLeg = Math.Max(minLeg + 10.0, tHohmannDays * maxFactor);
                    bounds.Add((minLeg, maxLeg));
                }
            }

            return bounds;
        }

        /// <summary>
        /// 全域弹性 ToF 自适应推导求解器 (将 ToF 上下限全量纳入优化求解范围)
        /// 机制: 依据全链条各航段物理开普勒共振与霍曼转移弧段动态汇聚全局物理闭合弹性窗口
        /// </summary>
        public static (double MinDays, double MaxDays) CalculateAdaptiveToFBounds(
            string departure, 
            string arrival, 
            List<string> flybyChain,
            string centralBody = "Sun",
            double depMinFactor = 0.65,
            double depMaxFactor = 1.50)
        {
            var legBounds = AutoDeriveResonantLegBounds(departure, arrival, flybyChain, centralBody, depMinFactor, depMaxFactor);
            double totalMinDays = 0.0;
            double totalMaxDays = 0.0;
            foreach (var b in legBounds)
            {
                totalMinDays += b.MinDays;
                totalMaxDays += b.MaxDays;
            }

            // 安全保底与上限裕度
            totalMinDays = Math.Max(50.0, totalMinDays);
            totalMaxDays = Math.Max(totalMinDays + 150.0, totalMaxDays * 1.15);

            return (totalMinDays, totalMaxDays);
        }

        private static List<List<string>> GetPermutations(List<string> list)
        {
            var result = new List<List<string>>();
            if (list == null || list.Count == 0) return result;
            if (list.Count == 1)
            {
                result.Add(new List<string>(list));
                return result;
            }
            for (int i = 0; i < list.Count; i++)
            {
                string current = list[i];
                var remaining = new List<string>(list);
                remaining.RemoveAt(i);
                foreach (var sub in GetPermutations(remaining))
                {
                    var p = new List<string> { current };
                    p.AddRange(sub);
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>
        /// 物理单调能量梯级判定 (IJCAI 单调梯级约束)
        /// 折叠连续同名天体(同星共振重复)后，若 SMA 序列保持全片单调(非降 或 非升)，则视为物理可达的梯级排序。
        /// 拦截外行星被排到内爬升体之前的非物理跳跃(如 Cassini 的 Earth➔Jupiter➔Venus)。
        /// </summary>
        private static bool IsSmaMonotonicLadder(List<string> chain)
        {
            if (chain == null || chain.Count < 2) return true;

            var collapsed = new List<double>();
            foreach (var body in chain)
            {
                var p = SolarSystemCatalog.GetPlanet(body);
                if (p == null || p.SMA <= 0) continue;
                if (collapsed.Count == 0 || Math.Abs(collapsed[collapsed.Count - 1] - p.SMA) > 1e-6)
                {
                    collapsed.Add(p.SMA);
                }
            }

            if (collapsed.Count < 2) return true;

            bool nonDecreasing = true, nonIncreasing = true;
            for (int i = 1; i < collapsed.Count; i++)
            {
                if (collapsed[i] < collapsed[i - 1]) nonDecreasing = false;
                if (collapsed[i] > collapsed[i - 1]) nonIncreasing = false;
                if (!nonDecreasing && !nonIncreasing) return false;
            }
            return true;
        }

        /// <summary>
        /// IJCAI 2015 开普勒历表周期离散网格生成器 (Ephemeris Grid)
        /// G = { t0 + j * (l * tau_i / 360°) }
        /// 将连续搜索时间按借力天体真实公转周期 tau_i 离散化，使同星共振和借力交会自然捕捉行星对齐切点
        /// </summary>
        public static List<double> GetEphemerisGridEpochs(string bodyName, double t0, double spanDays, double resolutionDeg = 22.5)
        {
            var epochs = new List<double>();
            var pBody = SolarSystemCatalog.GetPlanet(bodyName);
            if (pBody == null || pBody.SMA <= 0) return epochs;

            string central = pBody.ParentName ?? SolarSystemCatalog.RootStarName;
            double centralMu = SolarSystemCatalog.GetCentralMu(central);
            if (centralMu <= 0) centralMu = SolarSystemCatalog.SunMu;

            double sma = pBody.SMA;
            double periodSec = 2.0 * Math.PI * Math.Sqrt(Math.Pow(sma * 1000.0, 3.0) / (centralMu * 1e9));
            double periodDays = periodSec / 86400.0;
            if (periodDays <= 0 || double.IsNaN(periodDays)) periodDays = 365.25;

            double stepDays = periodDays * (resolutionDeg / 360.0);
            stepDays = Math.Max(1.0, stepDays);

            int steps = (int)Math.Ceiling(spanDays / stepDays);
            for (int j = 0; j <= steps; j++)
            {
                epochs.Add(t0 + j * stepDays * 86400.0);
            }
            return epochs;
        }

        /// <summary>
        /// 基于行星轨道内外侧拓扑与物理约束自动广度优先生成候选借力序列 (100% 对齐 KSP-MGA-Planner 拓扑搜索算法)
        /// </summary>
        public static List<List<string>> GenerateCandidateSequences(
            string depBody, string destBody, int maxSwingBys = 6, int maxResonances = 0, int maxBackLegs = 2, int maxBackSpacing = 2)
        {
            var cfg = new MGAConfig {
                DepartureBody = depBody,
                DestinationBody = destBody,
                MaxSwingBys = maxSwingBys,
                MaxResonances = maxResonances,
                MaxBackLegs = maxBackLegs,
                MaxBackSpacing = maxBackSpacing
            };
            return GenerateCandidateSequences(cfg);
        }

        public static List<List<string>> GenerateCandidateSequences(MGAConfig config) {
            var results = new List<List<string>>();
            if (config == null || string.IsNullOrEmpty(config.DepartureBody) || string.IsNullOrEmpty(config.DestinationBody))
                return results;

            string origin = config.DepartureBody;
            string dest = config.DestinationBody;

            // 出发与目标天体相同时无星际转移物理意义，直接返回空列表，杜绝无向 BFS 指数级爆炸与主线程卡死
            if (origin.Equals(dest, StringComparison.OrdinalIgnoreCase)) return results;

            var allPlanets = SolarSystemCatalog.GetAvailablePlanets();
            if (allPlanets == null || allPlanets.Count == 0) return results;

            // 过滤有效引力助推天体：
            // 仅保留出发天体、目标天体或引力常数 Mu >= 500.0 km^3/s^2 的有效引力源，
            // 彻底剔除 Ryugu, Eros, Bennu 等质量微小、偏转角近乎为0的微型小行星干扰，确保火星与金星排在队列首位
            var planetList = allPlanets.Where(p =>
                !string.IsNullOrEmpty(p.Name) &&
                (p.Name.Equals(origin, StringComparison.OrdinalIgnoreCase) ||
                 p.Name.Equals(dest, StringComparison.OrdinalIgnoreCase) ||
                 p.Mu >= 500.0)
            ).ToList();

            var bodyIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < planetList.Count; i++) {
                if (!string.IsNullOrEmpty(planetList[i].Name) && !bodyIndexMap.ContainsKey(planetList[i].Name)) {
                    bodyIndexMap[planetList[i].Name] = i;
                }
            }

            if (!bodyIndexMap.TryGetValue(origin, out int oIdx) || !bodyIndexMap.TryGetValue(dest, out int dIdx)) {
                return results;
            }

            var queue = new Queue<(List<string> chain, int resonances, int backLegs)>();
            queue.Enqueue((new List<string> { origin }, 0, 0));

            int maxLegs = config.MaxSwingBys + 1;
            int visitedCount = 0;

            var destPlanet = SolarSystemCatalog.GetPlanet(dest);
            bool destHasStrongGravity = (destPlanet != null && destPlanet.Mu >= 1000.0);

            while (queue.Count > 0) {
                // 安全熔断上限，严防异常分支造成游戏卡死
                if (++visitedCount > MGADefaults.MaxBfsVisitedCount || results.Count >= MGADefaults.MaxCandidateResults) break;

                var (currentChain, curRes, curBack) = queue.Dequeue();
                string currentBody = currentChain.Last();
                if (!bodyIndexMap.TryGetValue(currentBody, out int cIdx)) cIdx = -1;

                if (currentChain.Count > 1 && currentBody.Equals(dest, StringComparison.OrdinalIgnoreCase)) {
                    var subChain = new List<string>(currentChain.Skip(1));
                    if (!results.Any(r => r.SequenceEqual(subChain))) {
                        results.Add(subChain);
                    }

                    // 若目标天体自身具备显著引力 (如水星/火星/金星/地球/木星 Mu >= 1000) 且允许共振 (MaxResonances > 0)，
                    // 继续压入目标天体自身同星共振借力分支 (如 Earth -> Venus -> Mercury -> Mercury -> Mercury)，
                    // 彻底对齐 NASA MESSENGER / ESA BepiColombo 水星极低 ΔV 连续减速泊入历史经典任务！
                    if (destHasStrongGravity && (config.MaxResonances <= 0 || curRes < config.MaxResonances) && currentChain.Count < maxLegs + 1) {
                        var resonantChain = new List<string>(currentChain) { dest };
                        queue.Enqueue((resonantChain, curRes + 1, curBack));
                    }
                    continue;
                }

                if (currentChain.Count >= maxLegs + 1) continue;

                for (int i = 0; i < planetList.Count; i++) {
                    var pNext = planetList[i];
                    string nextBody = pNext.Name;
                    if (string.IsNullOrEmpty(nextBody)) continue;

                    bool isResonance = nextBody.Equals(currentBody, StringComparison.OrdinalIgnoreCase);
                    if (isResonance && config.MaxResonances > 0 && curRes >= config.MaxResonances) continue;

                    bool isBackLeg = (dIdx > oIdx) ? (i < cIdx) : (i > cIdx);
                    if (isBackLeg && curBack >= config.MaxBackLegs) continue;

                    int spacing = Math.Abs(i - cIdx);
                    if (isBackLeg && spacing > config.MaxBackSpacing) continue;

                    // Contraction 剪枝门禁 (IJCAI 2015): 凡二体转移或转角赤字超限的枝节点立即剪除整颗子树
                    if (!isResonance) {
                        var pCur = SolarSystemCatalog.GetPlanet(currentBody);
                        if (pCur != null && pNext != null && pCur.SMA > 0 && pNext.SMA > 0) {
                            double ratioSma = Math.Max(pCur.SMA, pNext.SMA) / Math.Min(pCur.SMA, pNext.SMA);
                            // 若非巨行星助推且单次半长轴跨度超 6 倍 (无中间跳板直飞外太阳系)，直接剪枝
                            if (ratioSma > 6.0 && pCur.Mu < 1e5 && pNext.Mu < 1e5) {
                                continue;
                            }
                        }
                    }

                    var newChain = new List<string>(currentChain) { nextBody };
                    queue.Enqueue((newChain, curRes + (isResonance ? 1 : 0), curBack + (isBackLeg ? 1 : 0)));
                }
            }

            // 确定性 Max 估计器 (IJCAI 2015): X = max(0, (DeltaV_max - DeltaV_tot) / DeltaV_max)
            double maxBudgetMs = (config.ConstrainByVesselDeltaV && config.VesselMaxDeltaVMs > 0) ? config.VesselMaxDeltaVMs : 4000.0;
            results = results.OrderByDescending(c => {
                if (c.Count == 1 && c[0].Equals(dest, StringComparison.OrdinalIgnoreCase)) return 0.5;

                double totalDeficitKms = 0.0;
                for (int idx = 0; idx < c.Count - 1; idx++) {
                    var pA = SolarSystemCatalog.GetPlanet(c[idx]);
                    var pB = SolarSystemCatalog.GetPlanet(c[idx + 1]);
                    if (pA != null && pB != null && pA.SMA > 0 && pB.SMA > 0) {
                        if (!c[idx].Equals(c[idx + 1], StringComparison.OrdinalIgnoreCase)) {
                            double rRatio = Math.Max(pA.SMA, pB.SMA) / Math.Min(pA.SMA, pB.SMA);
                            if (rRatio > 4.0 && pA.Mu < 1e5) totalDeficitKms += (rRatio - 4.0) * 1.5;
                        }
                    }
                }
                double estimatedTotalMs = 3800.0 + totalDeficitKms * 1000.0;
                double xScore = Math.Max(0.0, (maxBudgetMs - estimatedTotalMs) / maxBudgetMs);
                return xScore;
            }).ToList();

            return results;
        }
    }
}
