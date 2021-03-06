// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.LanguageServices.CSharp.ProjectSystemShim.Interop;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.CSharp.ProjectSystemShim
{
    /// <summary>
    /// An implementation a TempPE compiler that is called by the project system.
    ///
    /// This class is free-threaded.
    /// </summary>
    internal class TempPECompilerService : ICSharpTempPECompilerService
    {
        private readonly VisualStudioWorkspace _workspace;

        public TempPECompilerService(VisualStudioWorkspace workspace)
        {
            _workspace = workspace;
        }

        public void CompileTempPE(string pszOutputFileName, int sourceCount, string[] fileNames, string[] fileContents, int optionCount, string[] optionNames, object[] optionValues)
        {
            var baseDirectory = Path.GetDirectoryName(pszOutputFileName);
            var parsedArguments = ParseCommandLineArguments(baseDirectory, optionNames, optionValues);

            Contract.ThrowIfFalse(fileNames.Length == fileContents.Length);

            var trees = new List<SyntaxTree>(capacity: sourceCount);

            for (int i = 0; i < fileNames.Length; i++)
            {
                // create a parse tree w/o encoding - the tree won't be used to emit PDBs
                trees.Add(SyntaxFactory.ParseSyntaxTree(fileContents[i], parsedArguments.ParseOptions, fileNames[i]));
            }

            // TODO (tomat): Revisit compilation options: App.config, strong name, search paths, etc? (bug #869604)
            // TODO (tomat): move resolver initialization (With* methods below) to CommandLineParser.Parse

            var metadataProvider = _workspace.Services.GetService<IMetadataService>().GetProvider();
            var metadataResolver = new AssemblyReferenceResolver(MetadataFileReferenceResolver.Default, metadataProvider);

            var compilation = CSharpCompilation.Create(
                Path.GetFileName(pszOutputFileName),
                trees,
                parsedArguments.ResolveMetadataReferences(metadataResolver),
                parsedArguments.CompilationOptions
                    .WithAssemblyIdentityComparer(DesktopAssemblyIdentityComparer.Default)
                    .WithSourceReferenceResolver(SourceFileResolver.Default)
                    .WithXmlReferenceResolver(XmlFileResolver.Default)
                    .WithMetadataReferenceResolver(metadataResolver));

            compilation.Emit(pszOutputFileName);
        }

        private CSharpCommandLineArguments ParseCommandLineArguments(string baseDirectory, string[] optionNames, object[] optionValues)
        {
            Contract.ThrowIfFalse(optionNames.Length == optionValues.Length);

            var arguments = new List<string>();

            for (int i = 0; i < optionNames.Length; i++)
            {
                if (optionNames[i] == "r")
                {
                    // We get a pipe-delimited list of references, so split them back apart
                    foreach (var reference in ((string)optionValues[i]).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        arguments.Add(string.Format("/r:\"{0}\"", reference));
                    }
                }
                else
                {
                    arguments.Add(string.Format("/{0}:{1}", optionNames[i], optionValues[i]));
                }
            }

            return CSharpCommandLineParser.Default.Parse(arguments, baseDirectory);
        }
    }
}
