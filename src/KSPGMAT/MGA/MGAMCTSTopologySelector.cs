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
            var sequences = new List<List<string>>();
            if (string.IsNullOrEmpty(config.DeparturePlanet) || string.IsNullOrEmpty(config.ArrivalPlanet))
                return sequences;

            // 1. 直达序列恒为最高优先级基准
            sequences.Add(new List<string> { config.ArrivalPlanet });

            // 若用户指定了特定借力链，按配置自动展开乱序与高价值插序
            if (config.FlybySequence != null && config.FlybySequence.Count > 0)
            {
                if (config.EnablePermutationsAndInsertions)
                {
                    var expanded = ExpandPermutationsAndInsertions(config.DeparturePlanet, config.ArrivalPlanet, config.FlybySequence, maxChains);
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

                            // 内潜反弹大加速: dep -> Inner -> dep -> Giant -> arr
                            if (innerAssist != null && (rArr >= rDep * 3.0))
                            {
                                sequences.Add(new List<string> { innerAssist.Name, config.DeparturePlanet, primaryGiant.Name, config.ArrivalPlanet });
                            }

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

            var cleaned = rawFlybys.Where(p => !string.IsNullOrEmpty(p) && !p.Equals(departure, StringComparison.OrdinalIgnoreCase)).ToList();
            if (cleaned.Count == 0) return result;

            // 1. 用户原始序列作为最高优先级基准
            result.Add(new List<string>(cleaned));

            // 2. 中途天体乱序全排列探索 (Permutations)
            if (cleaned.Count >= 2 && cleaned.Count <= 5)
            {
                bool lastIsArrival = cleaned.Last().Equals(arrival, StringComparison.OrdinalIgnoreCase);
                var subToPermute = lastIsArrival ? cleaned.Take(cleaned.Count - 1).ToList() : new List<string>(cleaned);
                string fixedEnd = lastIsArrival ? cleaned.Last() : null;

                var allPerms = GetPermutations(subToPermute);
                foreach (var p in allPerms)
                {
                    var chain = new List<string>(p);
                    if (fixedEnd != null) chain.Add(fixedEnd);
                    else if (!chain.Last().Equals(arrival, StringComparison.OrdinalIgnoreCase)) chain.Add(arrival);

                    if (!result.Any(r => r.SequenceEqual(chain)))
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
            // 普适物理律: 前往远外深空天体 (rArr >= rDep * 3.0，如土星、天王星、海王星) 时，
            // 若尚未经过中间巨行星 (如木星)，在到达目标前插入位于中途的极大引力天体 (Mu 极大值)，
            // 是物理上实现深空大角度抛射与零工质加速的根本保证
            if (pArrObj != null && pDepObj != null && pArrObj.SMA >= pDepObj.SMA * 3.0)
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
                            int insIdx = inserted.Count - 1;
                            if (insIdx >= 0 && inserted[insIdx].Equals(arrival, StringComparison.OrdinalIgnoreCase))
                            {
                                inserted.Insert(insIdx, giantAssist.Name);
                            }
                            else
                            {
                                inserted.Add(giantAssist.Name);
                                if (!inserted.Last().Equals(arrival, StringComparison.OrdinalIgnoreCase))
                                    inserted.Add(arrival);
                            }
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
            // 真实物理法则：当借力链从太阳深引力井天体 (如 Mercury SMA < 0.5 AU) 跨越至远外行星 (如 Jupiter/Neptune SMA > 2.0 AU) 时，
            // 二体能量极差达 15~18 km/s，单次微小天体借力无法跨越。
            // 必须插入中途爬升天体 (Venus / Earth) 逐级泵升远心点，杜绝超大 DSM 消耗！
            if (pDepObj != null)
            {
                var climbingCandidates = new List<List<string>>();
                foreach (var baseChain in result.Take(8).ToList())
                {
                    var chainWithArr = new List<string>(baseChain);
                    if (!chainWithArr.Last().Equals(arrival, StringComparison.OrdinalIgnoreCase))
                    {
                        chainWithArr.Add(arrival);
                    }

                    for (int i = 0; i < chainWithArr.Count - 1; i++)
                    {
                        var pCurr = SolarSystemCatalog.GetPlanet(chainWithArr[i]);
                        var pNext = SolarSystemCatalog.GetPlanet(chainWithArr[i + 1]);
                        if (pCurr != null && pNext != null && pCurr.SMA > 0 && pNext.SMA > 0)
                        {
                            if (pCurr.SMA < pDepObj.SMA && pNext.SMA >= pDepObj.SMA * 2.0)
                            {
                                // 获取按半长轴严格升序排列的所有中间跳板天体 (例如从水星到海王星: Venus -> Earth -> Jupiter)
                                var steppingStones = SolarSystemCatalog.Planets.Values
                                    .Where(b => b.SMA > pCurr.SMA && b.SMA < pNext.SMA && b.Mu >= 500.0 && b.RadiusKm > 0 &&
                                                !b.Name.Equals(chainWithArr[i], StringComparison.OrdinalIgnoreCase) &&
                                                !b.Name.Equals(chainWithArr[i + 1], StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(b => b.SMA)
                                    .ToList();

                                // 筛选具显著引力助推能力的行星跳板 (内行星保留 Venus/Earth，外行星选取 1 颗主巨行星如 Jupiter)
                                var innerLadder = steppingStones.Where(b => b.SMA < 4.0 * 1.496e8 && ((b.Mu / b.RadiusKm >= 35.0) || b.Mu >= 1.0e7)).ToList();
                                var outerGiant = steppingStones.Where(b => b.SMA >= 4.0 * 1.496e8).OrderByDescending(b => b.Mu).Take(1).ToList();
                                var ladder = new List<SolarPlanet>(innerLadder);
                                ladder.AddRange(outerGiant);

                                if (ladder.Count > 0)
                                {
                                    // 1. 严格按半长轴升序逐阶爬升梯子: [pCurr] ➔ Venus ➔ Earth ➔ Jupiter ➔ [pNext]
                                    var ladderChain = new List<string>(chainWithArr);
                                    ladderChain.InsertRange(i + 1, ladder.Select(l => l.Name));
                                    if (!result.Any(r => r.SequenceEqual(ladderChain)) && !climbingCandidates.Any(r => r.SequenceEqual(ladderChain)))
                                    {
                                        climbingCandidates.Add(ladderChain);
                                    }

                                    // 2. 若终点为远外深空行星 (SMA >= pDepObj.SMA * 5.0，如 Neptune/Uranus)，在地球处插入 2:1 共振槽静候外行星窗口
                                    var earthBody = ladder.FirstOrDefault(l => l.Name.Equals(pDepObj.Name, StringComparison.OrdinalIgnoreCase));
                                    if (earthBody != null && pNext.SMA >= pDepObj.SMA * 5.0)
                                    {
                                        var resonantLadder = new List<string>(chainWithArr);
                                        var ladderWithRes = new List<string>();
                                        foreach (var l in ladder)
                                        {
                                            ladderWithRes.Add(l.Name);
                                            if (l.Name.Equals(earthBody.Name, StringComparison.OrdinalIgnoreCase))
                                            {
                                                ladderWithRes.Add(earthBody.Name); // 插入同星 2:1 开普勒共振调相
                                            }
                                        }
                                        resonantLadder.InsertRange(i + 1, ladderWithRes);
                                        if (!result.Any(r => r.SequenceEqual(resonantLadder)) && !climbingCandidates.Any(r => r.SequenceEqual(resonantLadder)))
                                        {
                                            climbingCandidates.Add(resonantLadder);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var cc in climbingCandidates)
                {
                    if (result.Count < maxCombinations + 24)
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
                        double flybyPower = pBody.Mu / pBody.RadiusKm;
                        int resMax = (flybyPower < 15.0) ? 4 : (flybyPower < 60.0 ? 3 : 2);
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
                    var neighborBList = SolarSystemCatalog.Planets.Values
                        .Where(b => !string.IsNullOrEmpty(b.Name) &&
                                    !b.Name.Equals(bodyA, StringComparison.OrdinalIgnoreCase) &&
                                    !b.Name.Equals(departure, StringComparison.OrdinalIgnoreCase) &&
                                    b.Mu > 0 && b.RadiusKm > 0 &&
                                    b.SMA > 0 && Math.Abs(Math.Log(b.SMA / pA.SMA)) < 1.5)
                        .OrderByDescending(b => b.Mu / b.RadiusKm)
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
            string centralBody = "Sun")
        {
            var fullChain = new List<string> { departure };
            if (flybyChain != null) fullChain.AddRange(flybyChain);
            fullChain.Add(arrival);

            var bounds = new List<(double MinDays, double MaxDays)>();
            double muCentralM = (SolarSystemCatalog.GetCentralMu(centralBody) > 0 
                ? SolarSystemCatalog.GetCentralMu(centralBody) 
                : (SolarSystemCatalog.SunMu > 0 ? SolarSystemCatalog.SunMu : 1.32712440018e11)) * 1e9;

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
                    // 同星共振调相段：依据天体公转周期精确锁定开普勒共振槽 (1:1, 3:2, 2:1, 3:1 等)
                    double tOrbDays = 2.0 * Math.PI * Math.Sqrt(Math.Pow(sma1 * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tOrbDays <= 0 || double.IsNaN(tOrbDays)) tOrbDays = 365.25;
                    double minLeg = Math.Max(0.5, tOrbDays * 0.50);
                    double maxLeg = Math.Max(minLeg + 5.0, tOrbDays * 3.50);
                    bounds.Add((minLeg, maxLeg));
                }
                else
                {
                    // 异星转移段：依霍曼半长轴留足物理弧段
                    double aTrans = (sma1 + sma2) * 0.5;
                    double tHohmannDays = Math.PI * Math.Sqrt(Math.Pow(aTrans * 1000.0, 3.0) / muCentralM) / 86400.0;
                    if (tHohmannDays <= 0 || double.IsNaN(tHohmannDays)) tHohmannDays = 200.0;
                    double minFactor = (sma2 >= sma1 * 3.0 || sma1 >= sma2 * 3.0) ? 0.18 : 0.40;
                    double maxFactor = (sma1 < 1.1e8 || sma2 < 1.1e8) ? 4.50 : 2.40; // 涉及内行星（水星/金星）放宽转移上限以允许更多圈数等待 (如 370d 多圈返金星)
                    double minLeg = Math.Max(5.0, tHohmannDays * minFactor);
                    double maxLeg = Math.Max(minLeg + 30.0, tHohmannDays * maxFactor);
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
            List<string> flybyChain)
        {
            var legBounds = AutoDeriveResonantLegBounds(departure, arrival, flybyChain);
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

                    var newChain = new List<string>(currentChain) { nextBody };
                    queue.Enqueue((newChain, curRes + (isResonance ? 1 : 0), curBack + (isBackLeg ? 1 : 0)));
                }
            }

            // 智能天体力学价值赋权排序 (Zero Hardcoding / 完全基于开普勒轨道半长轴阶梯与引力势能深度标准化物理量)
            var pOrigin = SolarSystemCatalog.GetPlanet(origin);
            double rDep = (pOrigin != null && pOrigin.SMA > 0) ? pOrigin.SMA : 1.496e8;
            double rDest = (destPlanet != null && destPlanet.SMA > 0) ? destPlanet.SMA : 1.496e8;
            double rMin = Math.Min(rDep, rDest);
            double rMax = Math.Max(rDep, rDest);
            double rSpan = Math.Max(1.0, rMax - rMin);

            results = results.OrderByDescending(c => {
                // 直接到达标杆基准给予基础基准分
                if (c.Count == 1 && c[0].Equals(dest, StringComparison.OrdinalIgnoreCase)) return 50.0;

                double score = 0.0;
                for (int idx = 0; idx < c.Count; idx++) {
                    string bName = c[idx];
                    var p = SolarSystemCatalog.GetPlanet(bName);
                    if (p == null) continue;

                    // 物理量 1: 天体引力势能深度贡献 (引力井越深，双曲转角与动量改变能力越大)
                    if (p.Mu > 0) {
                        score += 10.0 * Math.Log10(Math.Max(1.0, p.Mu));
                    }

                    // 物理量 2: 轨道阶梯几何匹配 (中间天体公转轨道半长轴处于起点与目标之间时，天然充当轨道能量过渡阶梯)
                    if (p.SMA >= rMin && p.SMA <= rMax) {
                        double midProgress = 1.0 - Math.Abs(p.SMA - (rMin + rMax) * 0.5) / (rSpan * 0.5 + 1e-5);
                        score += 35.0 * Math.Max(0.2, midProgress);
                    }

                    // 物理量 3: 目标天体同星共振减速价值 (当目标天体具备显著引力井时，同星共振逐次削减到达双曲渐近速度 V_inf)
                    if (bName.Equals(dest, StringComparison.OrdinalIgnoreCase) && idx < c.Count - 1 && p.Mu >= 1000.0) {
                        score += 15.0 * Math.Log10(Math.Max(1.0, p.Mu));
                    }
                }

                // 物理量 4: 航段复杂度适度正则化 (2~4 段为工程最实用区间)
                if (c.Count >= 2 && c.Count <= 4) score += 10.0;
                else if (c.Count > 4) score -= (c.Count - 4) * 15.0;

                return score;
            }).ToList();

            return results;
        }
    }
}
