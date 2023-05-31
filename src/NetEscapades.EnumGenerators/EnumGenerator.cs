using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace NetEscapades.EnumGenerators;

[Generator]
public class EnumGenerator : ISourceGenerator
{
    private const string DisplayAttribute = "System.ComponentModel.DataAnnotations.DisplayAttribute";
    private const string DescriptionAttribute = "System.ComponentModel.DescriptionAttribute";
    private const string EnumExtensionsAttribute = "NetEscapades.EnumGenerators.EnumExtensionsAttribute";
    private const string FlagsAttribute = "System.FlagsAttribute";

    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForPostInitialization(ctx => ctx.AddSource(
            "EnumExtensionsAttribute.g.cs", SourceText.From(SourceGenerationHelper.Attribute, Encoding.UTF8)));
        
        context.RegisterForSyntaxNotifications(() => new EnumGeneratorSyntaxReceiver());
    }

    public void Execute(GeneratorExecutionContext context)
    {
        try
        {
            if (context.SyntaxReceiver is not EnumGeneratorSyntaxReceiver syntaxReceiver)
                return;

            foreach (var target in syntaxReceiver.Targets)
            {
                // get target type info
                var semanticModel = context.Compilation.GetSemanticModel(target.SyntaxTree);
                var typeSymbol = semanticModel.GetDeclaredSymbol(target, context.CancellationToken);
                if (typeSymbol == null)
                    throw new Exception("Can not get type symbol");

                var enumToGenerate = GetTypeToGenerate(typeSymbol, context.CancellationToken);
                GenerateEnum(enumToGenerate, context);
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.TraceError(e.Message);
        }
    }

    private static void GenerateEnum(in EnumToGenerate? enumToGenerate, GeneratorExecutionContext context)
    {
        if (enumToGenerate is { } eg)
        {
            var sb = new StringBuilder();
            var result = SourceGenerationHelper.GenerateExtensionClass(sb, in eg);
            context.AddSource(eg.Name + "_EnumExtensions.g.cs", SourceText.From(result, Encoding.UTF8));    
        }
    }

    private static EnumToGenerate? GetTypeToGenerate(ISymbol symbol, CancellationToken ct)
    {
        if (symbol is not INamedTypeSymbol enumSymbol)
        {
            // nothing to do if this type isn't available
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var name = enumSymbol.Name + "Extensions";
        var nameSpace = enumSymbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : enumSymbol.ContainingNamespace.ToString();
        var hasFlags = false;

        foreach (var attributeData in enumSymbol.GetAttributes())
        {
            if (attributeData.AttributeClass?.Name is "FlagsAttribute" or "Flags" &&
                attributeData.AttributeClass.ToDisplayString() == FlagsAttribute)
            {
                hasFlags = true;
                continue;
            }

            if (attributeData.AttributeClass?.Name != "EnumExtensionsAttribute" ||
                attributeData.AttributeClass.ToDisplayString() != EnumExtensionsAttribute)
            {
                continue;
            }

            foreach (var namedArgument in attributeData.NamedArguments)
            {
                switch (namedArgument.Key)
                {
                    case "ExtensionClassNamespace" 
                    when namedArgument.Value.Value?.ToString() is { } ns:
                        nameSpace = ns;
                        continue;
                    case "ExtensionClassName" 
                    when namedArgument.Value.Value?.ToString() is { } n:
                        name = n;
                        break;
                }
            }
        }

        var fullyQualifiedName = enumSymbol.ToString();
        var underlyingType = enumSymbol.EnumUnderlyingType?.ToString() ?? "int";

        var enumMembers = enumSymbol.GetMembers();
        var members = new List<(string, EnumValueOption)>(enumMembers.Length);
        HashSet<string>? displayNames = null;
        var isDisplayNameTheFirstPresence = false;

        foreach (var member in enumMembers)
        {
            if (member is not IFieldSymbol field
                || field.ConstantValue is null)
            {
                continue;
            }

            string? displayName = null;
            foreach (var attribute in member.GetAttributes())
            {
                if (attribute.AttributeClass?.Name == "DisplayAttribute" &&
                    attribute.AttributeClass.ToDisplayString() == DisplayAttribute)
                {
                    foreach (var namedArgument in attribute.NamedArguments)
                    {
                        if (namedArgument.Key == "Name" && namedArgument.Value.Value?.ToString() is { } dn)
                        {
                            // found display attribute, all done
                            displayName = dn;
                            goto addDisplayName;
                        }
                    }
                }
                
                if (attribute.AttributeClass?.Name == "DescriptionAttribute" 
                    && attribute.AttributeClass.ToDisplayString() == DescriptionAttribute
                    && attribute.ConstructorArguments.Length == 1)
                {
                    if (attribute.ConstructorArguments[0].Value?.ToString() is { } dn)
                    {
                        // found display attribute, all done
                        displayName = dn;
                        goto addDisplayName;
                    }
                }
            }

            addDisplayName:
            if (displayName is not null)
            {
                displayNames ??= new();
                isDisplayNameTheFirstPresence = displayNames.Add(displayName);    
            }
            
            members.Add((member.Name, new EnumValueOption(displayName, isDisplayNameTheFirstPresence)));
        }

        return new EnumToGenerate(
            name: name,
            fullyQualifiedName: fullyQualifiedName,
            ns: nameSpace,
            underlyingType: underlyingType,
            isPublic: enumSymbol.DeclaredAccessibility == Accessibility.Public,
            hasFlags: hasFlags,
            names: members,
            isDisplayAttributeUsed: displayNames?.Count > 0);
    }
}
