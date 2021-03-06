using Dman.LSystem.SystemCompiler;
using Dman.LSystem.SystemRuntime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dman.LSystem
{
    public static class LSystemBuilder
    {
        /// <summary>
        /// Compile a new L-system from rule text
        /// </summary>
        /// <param name="rules">a list of all of the rules in this L-System</param>
        /// <param name="globalParameters">A list of global parameters.
        ///     The returned LSystem will require a double[] of the same length be passed in to the step function</param>
        /// <returns></returns>
        public static LSystem<double> DoubleSystem(
           IEnumerable<string> rules,
           string[] globalParameters = null,
           string ignoredCharacters = "")
        {
            return new LSystem<double>(
                RuleParser.CompileRules(
                        rules,
                        globalParameters
                        ),
                globalParameters?.Length ?? 0,
                ignoredCharacters: new HashSet<int>(ignoredCharacters.Select(x => (int)x))
            );
        }
    }

    public class LSystemState<T>
    {
        public SymbolString<T> currentSymbols { get; set; }
        public Unity.Mathematics.Random randomProvider;
    }

    public class DefaultLSystemState : LSystemState<double>
    {
        public DefaultLSystemState(string axiom, int seed): this(axiom, (uint)seed)
        {}
        public DefaultLSystemState(string axiom, uint seed = 1)
        {
            currentSymbols = new SymbolString<double>(axiom);
            randomProvider = new Unity.Mathematics.Random(seed);
        }
    }

    public class LSystem<T>
    {
        /// <summary>
        /// structured data to store rules, in order of precidence, as follows:
        ///     first, order by size of the TargetSymbolSeries. Patterns which match more symbols always take precidence over patterns which match less symbols
        /// </summary>
        private IDictionary<int, IList<IRule<T>>> rulesByTargetSymbol;

        /// <summary>
        /// The number of global runtime parameters
        /// </summary>
        public int GlobalParameters { get; private set; }

        public int branchOpenSymbol;
        public int branchCloseSymbol;
        /// <summary>
        /// Defaults to false. fully ordering agnostic matching is not yet implemented, setting to true will result in an approximation
        ///     with some failures on edge cases involving subsets of matches. look at the context matcher tests for more details.
        /// </summary>
        public bool orderingAgnosticContextMatching = false;

        // currently just used for blocking out context matching. could be used in the future to exclude rule application from specific symbols, too.
        // if that improves runtime.
        public ISet<int> ignoredCharacters;

        public LSystem(
            IEnumerable<IRule<T>> rules,
            int expectedGlobalParameters = 0,
            int branchOpenSymbol = '[',
            int branchCloseSymbol = ']',
            ISet<int> ignoredCharacters = null)
        {
            GlobalParameters = expectedGlobalParameters;
            this.branchOpenSymbol = branchOpenSymbol;
            this.branchCloseSymbol = branchCloseSymbol;
            this.ignoredCharacters = ignoredCharacters == null ? new HashSet<int>() : ignoredCharacters;

            rulesByTargetSymbol = new Dictionary<int, IList<IRule<T>>>();
            foreach (var rule in rules)
            {
                var targetSymbols = rule.TargetSymbol;
                if (!rulesByTargetSymbol.TryGetValue(targetSymbols, out var ruleList))
                {
                    rulesByTargetSymbol[targetSymbols] = ruleList = new List<IRule<T>>();
                }
                ruleList.Add(rule);
            }
            foreach (var symbol in rulesByTargetSymbol.Keys.ToList())
            {
                rulesByTargetSymbol[symbol] = rulesByTargetSymbol[symbol]
                    .OrderByDescending(x => (x.ContextPrefix?.targetSymbolSeries?.Length ?? 0) + (x.ContextSuffix?.targetSymbolSeries?.Length ?? 0))
                    .ToList();
            }
        }

        /// <summary>
        /// Step the given <paramref name="systemState"/>. returning the new system state. No modifications are made the the system sate
        /// </summary>
        /// <param name="systemState">The entire state of the L-system. no modifications are made to this object or the contained properties.</param>
        /// <param name="globalParameters">The global parameters, if any</param>
        public LSystemState<T> StepSystem(LSystemState<T> systemState, T[] globalParameters = null)
        {
            UnityEngine.Profiling.Profiler.BeginSample("L system step");
            var globalParamSize = globalParameters?.Length ?? 0;
            if (globalParamSize != GlobalParameters)
            {
                throw new LSystemRuntimeException($"Incomplete parameters provided. Expected {GlobalParameters} parameters but got {globalParamSize}");
            }
            var nextState = new LSystemState<T>()
            {
                randomProvider = systemState.randomProvider
            };
            var resultString = GenerateNextSymbols(systemState.currentSymbols, ref nextState.randomProvider, globalParameters).ToList();
            nextState.currentSymbols = SymbolString<T>.ConcatAll(resultString);
            UnityEngine.Profiling.Profiler.EndSample();
            return nextState;
        }

        private SymbolString<T>[] GenerateNextSymbols(SymbolString<T> symbolState, ref Unity.Mathematics.Random random, T[] globalParameters)
        {
            var tmpBranchingCache = new SymbolStringBranchingCache(branchOpenSymbol, branchCloseSymbol, this.ignoredCharacters);
            tmpBranchingCache.SetTargetSymbolString(symbolState);

            var resultArray = new SymbolString<T>[symbolState.symbols.Length];
            for (int symbolIndex = 0; symbolIndex < symbolState.symbols.Length;)
            {
                var symbol = symbolState.symbols[symbolIndex];
                var parameters = symbolState.parameters[symbolIndex];
                var ruleApplied = false;
                if (rulesByTargetSymbol.TryGetValue(symbol, out var ruleList) && ruleList != null && ruleList.Count > 0)
                {
                    foreach (var rule in ruleList)
                    {
                        // check if match
                        var result = rule.ApplyRule(
                            tmpBranchingCache,
                            symbolState,
                            symbolIndex,
                            ref random,
                            globalParameters);// todo
                        if (result != null)
                        {
                            resultArray[symbolIndex] = result;
                            symbolIndex += 1;
                            ruleApplied = true;
                            break;
                        }
                    }
                }
                if (!ruleApplied)
                {
                    // if none of the rules match, which could happen if all of the matches for this char require additional subsequent characters
                    // or if there are no rules
                    resultArray[symbolIndex] = new SymbolString<T>(symbol, parameters);
                    symbolIndex++;
                }
            }
            return resultArray;
        }

        /// <summary>
        /// check to make sure <paramref name="targetSeries"/> matches <paramref name="symbols"/>, starting at <paramref name="offset"/> inside <paramref name="symbols"/>
        /// </summary>
        /// <param name="symbols"></param>
        /// <param name="targetSeries"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        private bool MatchesSymbolStringAfterFirst(
            int[] symbols,
            int[] targetSeries,
            int offset)
        {
            if (targetSeries.Length == 1)
            {
                return true;
            }
            for (int i = 1; i < targetSeries.Length; i++)
            {
                if (i + offset >= symbols.Length)
                {
                    return false;
                }
                if (symbols[i + offset] != targetSeries[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
