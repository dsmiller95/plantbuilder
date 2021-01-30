using System;

namespace Dman.LSystem.SystemRuntime
{
    public interface IRule<T>
    {
        /// <summary>
        /// the symbols which this rule will replace. Apply rule will only ever be called when replacing these exact symbols.
        /// This property will always return the same value for any given instance. It can be used to index the rule
        /// </summary>
        public int[] TargetSymbolSeries { get; }

        /// <summary>
        /// retrun the symbol string to replace the rule's matching symbols with. return null if no match
        /// </summary>
        /// <param name="parameters">the parameters applied to the symbol,
        ///     indexed based on matches inside the target symbol series.
        ///     Could be an array of null if no parameters.
        ///     Will always be the same length as what is returned from TargetSymbolSeries</param>
        /// <returns></returns>
        public SymbolString<T> ApplyRule(
            ArraySegment<T[]> symbolParameters,
            System.Random random,
            T[] globalParameters);
    }
}