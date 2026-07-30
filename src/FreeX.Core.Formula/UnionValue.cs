using FreeX.Core.Model;

namespace FreeX.Core.Formula;

/// <summary>
/// Represents Excel's UNION reference -- e.g. the value of <c>(A1:B2,D5,F1:F10)</c> -- as a list
/// of independent <see cref="RangeValue"/> areas.
/// </summary>
/// <remarks>
/// This is deliberately a FreeX.Core.Formula-project-only <see cref="ScalarValue"/> subtype
/// rather than a change to the shared <c>FreeX.Core.Model.ScalarValue</c> hierarchy: R85's
/// investigation found a real multi-area value kind would ripple through 15+ files across the
/// evaluator, <see cref="FormulaRewriter"/>'s row/col-shift logic, and <see cref="FormulaSerializer"/>'s
/// round-trip, plus every cell-value consumer outside the formula engine (IO writers/readers,
/// UI value formatting) that switches over the small, closed set of Core.Model ScalarValue kinds.
/// A <see cref="UnionValue"/> only ever exists transiently, as the evaluated result of a
/// <see cref="UnionNode"/> flowing into a function argument (<see cref="BuiltInFunctions.Areas"/>
/// et al.) or a top-level formula result (coerced to <c>#VALUE!</c> by
/// <see cref="FormulaEvaluator"/>'s top-level normalization, matching Excel, which also rejects a
/// bare union reference as a cell's final value) -- it is never stored in a cell, serialized to a
/// file, or observed by any code outside FreeX.Core.Formula. See R93-AREAS-union-value-model.
/// </remarks>
public sealed record UnionValue(IReadOnlyList<RangeValue> Areas) : ScalarValue;
