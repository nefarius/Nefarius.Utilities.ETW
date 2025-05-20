using System.Diagnostics.CodeAnalysis;

using Kaitai;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class DbiSymbolExtensions
{
    public static IEnumerable<SymProc32AnnotationPair> EnumerateTmfAnnotations(
        this List<MsPdb.DbiSymbol> symbols
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