using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetEscapades.EnumGenerators;

public class EnumGeneratorSyntaxReceiver : ISyntaxReceiver
{
    public readonly List<EnumDeclarationSyntax> Targets = new();

    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
    {
        if (syntaxNode is not EnumDeclarationSyntax enumNode)
            return;
        
        if (enumNode.AttributeLists.Count == 0)
            return;
        
        var hasAttribute = enumNode.AttributeLists.SelectMany(x => x.Attributes)
            .Any(a => a.Name.ToString().Contains("EnumExtensions"));
            
        if (!hasAttribute)
            return;
        
        Targets.Add(enumNode);
    }
}