// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis.CSharp.Symbols;
using Microsoft.CodeAnalysis.CSharp.Symbols.Metadata.PE;
using Microsoft.CodeAnalysis.ExpressionEvaluator;
using Microsoft.VisualStudio.Debugger;
using Microsoft.VisualStudio.Debugger.Clr;

namespace Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator
{
    internal sealed class CSharpInstructionDecoder : InstructionDecoder<CSharpCompilation, MethodSymbol, PEModuleSymbol, TypeSymbol, TypeParameterSymbol>
    {
        // This string was not localized in the old EE.  We'll keep it that way
        // so as not to break consumers who may have been parsing frame names...
        private const string AnonymousMethodName = "AnonymousMethod";

        /// <summary>
        /// Singleton instance of <see cref="CSharpInstructionDecoder"/> (created using default constructor).
        /// </summary>
        internal static readonly CSharpInstructionDecoder Instance = new CSharpInstructionDecoder();

        private CSharpInstructionDecoder()
        {
        }

        private static readonly SymbolDisplayFormat s_propertyDisplayFormat = DisplayFormat.
            AddMemberOptions(SymbolDisplayMemberOptions.IncludeParameters).
            WithParameterOptions(SymbolDisplayParameterOptions.IncludeType);

        internal override void AppendFullName(StringBuilder builder, MethodSymbol method)
        {
            var displayFormat =
                ((method.MethodKind == MethodKind.PropertyGet) || (method.MethodKind == MethodKind.PropertySet)) ?
                    s_propertyDisplayFormat :
                    DisplayFormat;

            var parts = method.ToDisplayParts(displayFormat);
            var numParts = parts.Length;
            for (int i = 0; i < numParts; i++)
            {
                var part = parts[i];
                var displayString = part.ToString();

                switch (part.Kind)
                {
                    case SymbolDisplayPartKind.ClassName:
                        if (GeneratedNames.GetKind(displayString) != GeneratedNameKind.LambdaDisplayClass)
                        {
                            builder.Append(displayString);
                        }
                        else
                        {
                            // Drop any remaining display class name parts and the subsequent dot...
                            do
                            {
                                i++;
                            }
                            while ((i < numParts) && parts[i].Kind != SymbolDisplayPartKind.MethodName);
                            i--;
                        }
                        break;
                    case SymbolDisplayPartKind.MethodName:
                        GeneratedNameKind kind;
                        int openBracketOffset, closeBracketOffset;
                        if (GeneratedNames.TryParseGeneratedName(displayString, out kind, out openBracketOffset, out closeBracketOffset) &&
                            (kind == GeneratedNameKind.LambdaMethod))
                        {
                            builder.Append(displayString, openBracketOffset + 1, closeBracketOffset - openBracketOffset - 1); // source method name
                            builder.Append('.');
                            builder.Append(AnonymousMethodName);
                            // NOTE: The old implementation only appended the first ordinal number.  Since this is not useful
                            // in uniquely identifying the lambda, we'll append the entire ordinal suffix (which may contain
                            // multiple numbers, as well as '-' or '_').
                            builder.Append(displayString.Substring(closeBracketOffset + 2)); // ordinal suffix (e.g. "__1")
                        }
                        else
                        {
                            builder.Append(displayString);
                        }
                        break;
                    default:
                        builder.Append(displayString);
                        break;
                }
            }
        }

        internal override MethodSymbol ConstructMethod(MethodSymbol method, ImmutableArray<TypeParameterSymbol> typeParameters, ImmutableArray<TypeSymbol> typeArguments)
        {
            var methodArity = method.Arity;
            var methodArgumentStartIndex = typeParameters.Length - methodArity;
            var typeMap = new TypeMap(
                ImmutableArray.Create(typeParameters, 0, methodArgumentStartIndex),
                ImmutableArray.Create(typeArguments, 0, methodArgumentStartIndex));
            var substitutedType = typeMap.SubstituteNamedType(method.ContainingType);
            method = method.AsMember(substitutedType);
            if (methodArity > 0)
            {
                method = method.Construct(ImmutableArray.Create(typeArguments, methodArgumentStartIndex, methodArity));
            }
            return method;
        }

        internal override ImmutableArray<TypeParameterSymbol> GetAllTypeParameters(MethodSymbol method)
        {
            return method.GetAllTypeParameters();
        }

        internal override CSharpCompilation GetCompilation(DkmClrInstructionAddress instructionAddress)
        {
            var moduleInstance = instructionAddress.ModuleInstance;
            var appDomain = moduleInstance.AppDomain;
            var previous = appDomain.GetDataItem<CSharpMetadataContext>();
            var metadataBlocks = instructionAddress.Process.GetMetadataBlocks(appDomain);

            CSharpCompilation compilation;
            if (metadataBlocks.HaveNotChanged(previous))
            {
                compilation = previous.Compilation;
            }
            else
            {
                var dataItem = new CSharpMetadataContext(metadataBlocks);
                appDomain.SetDataItem(DkmDataCreationDisposition.CreateAlways, dataItem);
                compilation = dataItem.Compilation;
            }

            return compilation;
        }

        internal override MethodSymbol GetMethod(CSharpCompilation compilation, DkmClrInstructionAddress instructionAddress)
        {
            return compilation.GetSourceMethod(instructionAddress.ModuleInstance.Mvid, instructionAddress.MethodId.Token);
        }

        internal override TypeNameDecoder<PEModuleSymbol, TypeSymbol> GetTypeNameDecoder(CSharpCompilation compilation, MethodSymbol method)
        {
            Debug.Assert(method is PEMethodSymbol);
            return new EETypeNameDecoder(compilation, (PEModuleSymbol)method.ContainingModule);
        }
    }
}
