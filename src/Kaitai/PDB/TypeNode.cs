using Kaitai;

namespace Nefarius.Utilities.ETW.Kaitai.PDB;

internal record TypeNode(KaitaiStruct type)
{
    public IList<TypeNode> XRefs = new List<TypeNode>();
}