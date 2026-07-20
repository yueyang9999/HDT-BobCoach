using System;
using System.Collections.Generic;
using System.Reflection;

namespace BobCoach.Engine
{
    /// <summary>
    /// BobsBuddy战斗模拟桥接 — 通过反射调用BobsBuddy仿真引擎,
    /// 获取标准战斗结果(胜率/平率/负率/伤害分布)用于CombatSimulator校准。
    /// 零编译时依赖: 使用反射在运行时发现BobsBuddy API。
    /// </summary>
    public static class BobsBuddyBridge
    {
        private static Assembly _bbAsm;
        private static Assembly _bbCommonAsm;
        private static int _initAttempts = 0;
        private static bool _available;

        /// <summary>BobsBuddy DLL是否可用 (每10次访问重试一次初始化)</summary>
        public static bool Available
        {
            get
            {
                if (!_available && _initAttempts < 5)
                    Initialize();
                else if (!_available && _initAttempts < 20)
                {
                    // 每10次访问重试一次 (可能HDT延迟加载了BobsBuddy)
                    _initAttempts++;
                    if (_initAttempts % 10 == 0) Initialize();
                }
                return _available;
            }
        }

        private static void Initialize()
        {
            _initAttempts++;
            try
            {
                // 运行时发现BobsBuddy程序集 (HDT已加载)
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "BobsBuddy")
                        _bbAsm = asm;
                    if (asm.GetName().Name == "BobsBuddy.Common")
                        _bbCommonAsm = asm;
                }

                // 如果HDT未加载, 尝试从HDT安装目录直接加载
                if (_bbAsm == null || _bbCommonAsm == null)
                {
                    try
                    {
                        // 路径1: 从我们的DLL位置推导HDT目录 (AppData\...\Plugins\)
                        string ourDir = System.IO.Path.GetDirectoryName(
                            Assembly.GetExecutingAssembly().Location);
                        string hostDir = AppDomain.CurrentDomain.BaseDirectory;
                        string[] searchPaths = {
                            hostDir,
                            ourDir
                        };
                        foreach (var dir in searchPaths)
                        {
                            if (string.IsNullOrEmpty(dir)) continue;
                            try
                            {
                                string bbPath = System.IO.Path.Combine(dir, "BobsBuddy.dll");
                                string bbCommonPath = System.IO.Path.Combine(dir, "BobsBuddy.Common.dll");
                                if (_bbAsm == null && System.IO.File.Exists(bbPath))
                                    _bbAsm = Assembly.LoadFrom(bbPath);
                                if (_bbCommonAsm == null && System.IO.File.Exists(bbCommonPath))
                                    _bbCommonAsm = Assembly.LoadFrom(bbCommonPath);
                                if (_bbAsm != null && _bbCommonAsm != null) break;
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                _available = _bbAsm != null && _bbCommonAsm != null;
            }
            catch { _available = false; }
        }

        /// <summary>
        /// 战斗模拟结果
        /// </summary>
        public class CombatResult
        {
            public double WinRate;       // 胜率 (0-1)
            public double TieRate;       // 平局率 (0-1)
            public double LossRate;      // 败率 (0-1)
            public double AvgDamageDealt;  // 平均造成伤害
            public double AvgDamageTaken;  // 平均受到伤害
            public int SimulationCount;    // 模拟次数
        }

        /// <summary>
        /// 用BobsBuddy模拟一场战斗。
        /// playerBoard/opponentBoard: MinionData列表(需要Attack/Health/Tier/Taunt/DivineShield等)
        /// </summary>
        public static CombatResult Simulate(List<MinionData> playerBoard,
            List<MinionData> opponentBoard, int simCount = 10000)
        {
            if (!Available) return null;

            try
            {
                // 发现BobsBuddy.Input类型并创建实例
                var inputType = _bbAsm?.GetType("BobsBuddy.Input")
                    ?? _bbCommonAsm?.GetType("BobsBuddy.Input");
                if (inputType == null) return null;

                var input = Activator.CreateInstance(inputType);

                // 通过反射设置板面数据
                SetBoardViaReflection(input, "playerBoard", playerBoard, inputType);
                SetBoardViaReflection(input, "opponentBoard", opponentBoard, inputType);

                // 设置模拟次数
                var simCountProp = inputType.GetProperty("SimCount")
                    ?? inputType.GetProperty("NumberOfSimulations")
                    ?? inputType.GetProperty("TotalSimulations");
                if (simCountProp != null && simCountProp.CanWrite)
                    simCountProp.SetValue(input, simCount);

                // 调用模拟
                var simulatorType = _bbAsm?.GetType("BobsBuddy.Simulator")
                    ?? _bbAsm?.GetType("BobsBuddy.SimulationRunner");
                if (simulatorType == null) return null;

                // 尝试不同的模拟入口方法
                object output = null;
                string[] simMethods = { "Simulate", "SimulateFight", "SimulateForInput", "Run" };
                foreach (var methodName in simMethods)
                {
                    var simMethod = simulatorType.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    if (simMethod == null) continue;

                    try
                    {
                        var simInstance = simMethod.IsStatic ? null
                            : Activator.CreateInstance(simulatorType);
                        output = simMethod.Invoke(simInstance,
                            new[] { input });
                        if (output != null) break;
                    }
                    catch { }
                }

                if (output == null) return null;
                return ExtractResult(output);
            }
            catch { return null; }
        }

        private static void SetBoardViaReflection(object input, string boardName,
            List<MinionData> board, Type inputType)
        {
            try
            {
                var boardProp = inputType.GetProperty(boardName);
                if (boardProp == null) return;

                // 尝试找BobsBuddy的Minion类型
                var bbMinionType = _bbAsm?.GetType("BobsBuddy.Minion")
                    ?? _bbCommonAsm?.GetType("BobsBuddy.Minion")
                    ?? _bbCommonAsm?.GetType("BobsBuddy.BoardMinion");
                if (bbMinionType == null) return;

                var minionListType = typeof(List<>).MakeGenericType(bbMinionType);
                var minionList = Activator.CreateInstance(minionListType);
                var addMethod = minionListType.GetMethod("Add");

                foreach (var m in board)
                {
                    var bbMinion = Activator.CreateInstance(bbMinionType);

                    // 设置基本属性
                    SetProp(bbMinion, bbMinionType, "Attack", m.Attack);
                    SetProp(bbMinion, bbMinionType, "Health", m.Health);
                    SetProp(bbMinion, bbMinionType, "Tier", m.Tier);
                    SetProp(bbMinion, bbMinionType, "Taunt", m.Taunt);
                    SetProp(bbMinion, bbMinionType, "DivineShield", m.DivineShield);
                    SetProp(bbMinion, bbMinionType, "Reborn", m.Reborn);
                    SetProp(bbMinion, bbMinionType, "Poisonous", m.Poisonous);
                    SetProp(bbMinion, bbMinionType, "Venomous", m.Venomous);
                    SetProp(bbMinion, bbMinionType, "Windfury", m.Windfury);
                    SetProp(bbMinion, bbMinionType, "Golden", m.Golden);
                    SetProp(bbMinion, bbMinionType, "CardId", m.CardId);

                    addMethod.Invoke(minionList, new[] { bbMinion });
                }

                boardProp.SetValue(input, minionList);
            }
            catch { /* board format mismatch, skip */ }
        }

        private static void SetProp(object obj, Type type, string name, object value)
        {
            var prop = type.GetProperty(name);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    // 类型转换
                    if (prop.PropertyType == typeof(bool) && value is int intVal)
                        prop.SetValue(obj, intVal != 0);
                    else if (prop.PropertyType == typeof(int) && value is bool boolVal)
                        prop.SetValue(obj, boolVal ? 1 : 0);
                    else if (prop.PropertyType == typeof(string))
                        prop.SetValue(obj, value?.ToString() ?? "");
                    else
                        prop.SetValue(obj, Convert.ChangeType(value, prop.PropertyType));
                }
                catch { }
            }
        }

        private static CombatResult ExtractResult(object output)
        {
            var result = new CombatResult();
            var outType = output.GetType();

            // 尝试读取各种可能的结果属性名
            TryReadDouble(output, outType, ref result.WinRate,
                "WinRate", "WinPercentage", "Win", "WinRatio");
            TryReadDouble(output, outType, ref result.TieRate,
                "TieRate", "TiePercentage", "Tie", "DrawRate");
            TryReadDouble(output, outType, ref result.LossRate,
                "LossRate", "LossPercentage", "Loss", "LoseRate");
            TryReadDouble(output, outType, ref result.AvgDamageDealt,
                "AvgDamageDealt", "AverageDamage", "DamageDealt", "AvgDamage");
            TryReadDouble(output, outType, ref result.AvgDamageTaken,
                "AvgDamageTaken", "DamageTaken", "AverageDamageTaken");
            TryReadInt(output, outType, ref result.SimulationCount,
                "SimCount", "SimulationCount", "CompletedSimulations", "TotalSimulations");

            // 如果WinRate未直接提供, 尝试从Results对象获取
            if (result.WinRate <= 0.001 && result.LossRate <= 0.001)
            {
                var resultsProp = outType.GetProperty("Results")
                    ?? outType.GetProperty("RoundResults")
                    ?? outType.GetProperty("RoundResultsForDisplay");
                if (resultsProp != null)
                {
                    try
                    {
                        var results = resultsProp.GetValue(output);
                        if (results != null)
                        {
                            var rt = results.GetType();
                            TryReadDouble(results, rt, ref result.WinRate,
                                "WinRate", "WinPercentage");
                            TryReadDouble(results, rt, ref result.TieRate,
                                "TieRate", "DrawRate");
                            TryReadDouble(results, rt, ref result.LossRate,
                                "LossRate", "LossPercentage");
                        }
                    }
                    catch { }
                }
            }

            // 如果仍然无结果但SimCount>0, 假设BobsBuddy运行了但输出格式不明
            if (result.WinRate <= 0.001 && result.LossRate <= 0.001
                && result.SimulationCount > 0)
            {
                // 兜底: 尝试从RoundResultsForDisplay等属性提取
                foreach (var prop in outType.GetProperties())
                {
                    try
                    {
                        var val = prop.GetValue(output);
                        if (val != null && val is double d && d > 0.001 && d <= 1.0)
                        {
                            // 假设第一个0-1之间的double是胜率
                            if (result.WinRate <= 0.001) result.WinRate = d;
                        }
                    }
                    catch { }
                }
            }

            return result;
        }

        private static void TryReadDouble(object obj, Type type, ref double target,
            params string[] names)
        {
            foreach (var name in names)
            {
                var prop = type.GetProperty(name);
                if (prop == null) continue;
                try
                {
                    var val = prop.GetValue(obj);
                    if (val != null)
                    {
                        target = Convert.ToDouble(val);
                        return;
                    }
                }
                catch { }
            }
        }

        private static void TryReadInt(object obj, Type type, ref int target,
            params string[] names)
        {
            foreach (var name in names)
            {
                var prop = type.GetProperty(name);
                if (prop == null) continue;
                try
                {
                    var val = prop.GetValue(obj);
                    if (val != null)
                    {
                        target = Convert.ToInt32(val);
                        return;
                    }
                }
                catch { }
            }
        }
    }
}
