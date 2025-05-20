using Kaitai;

namespace Nefarius.Utilities.ETW.Deserializer.WPP.TMF;

public record SymProc32AnnotationPair(MsPdb.SymProc32 Proc, List<MsPdb.SymAnnotation> Annotations);