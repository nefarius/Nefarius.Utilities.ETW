using Kaitai;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

internal static class DbiSymbolExtensions
{
    /// <summary>
    ///     Walks every symbol in the list and yields a <see cref="WppTraceControl" /> for each
    ///     standalone <c>S_ANNOTATION</c> record whose first string is <c>"TMC:"</c>.  These records
    ///     are emitted by the WPP pre-processor — one per <c>WPP_DEFINE_CONTROL_GUID</c> declaration —
    ///     and are NOT paired with a preceding <c>S_GPROC32</c>, unlike the TMF call-site records.
    /// </summary>
    public static IEnumerable<WppTraceControl> ExtractTraceControls(
        this IReadOnlyList<MsPdb.DbiSymbol> symbols)
    {
        foreach (MsPdb.DbiSymbol sym in symbols)
        {
            if (sym.Data.Body is not MsPdb.SymAnnotation ann)
            {
                continue;
            }

            if (ann.Strings.Count < 3)
            {
                continue;
            }

            if (ann.Strings[0] != "TMC:")
            {
                continue;
            }

            if (!Guid.TryParse(ann.Strings[1], out Guid controlGuid))
            {
                continue;
            }

            yield return new WppTraceControl
            {
                ControlGuid = controlGuid,
                Name = ann.Strings[2],
                BitFlags = ann.Strings.Skip(3).ToList()
            };
        }
    }

    public static IEnumerable<SymProc32AnnotationPair> ExtractTmfAnnotations(
        this IReadOnlyList<MsPdb.DbiSymbol> symbols
    )
    {
        for (int i = 0; i < symbols.Count;)
        {
            if (symbols[i].Data.Body is MsPdb.SymProc32 proc)
            {
                List<MsPdb.SymAnnotation> annotations = new();
                i++; // Advance to check for annotations

                while (i < symbols.Count)
                {
                    if (symbols[i].Data.Body is MsPdb.SymAnnotation annotation)
                    {
                        annotations.Add(annotation);
                        i++;
                    }
                    else if (symbols[i].Data.Body is MsPdb.SymProc32)
                    {
                        // Next SymProc32 encountered – break to process it on the next loop iteration
                        break;
                    }
                    else
                    {
                        // Skip unrelated symbol types
                        i++;
                    }
                }

                yield return new SymProc32AnnotationPair(proc, annotations);
            }
            else
            {
                // Skip unrelated symbol types
                i++;
            }
        }
    }
}