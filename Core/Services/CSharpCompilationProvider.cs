﻿using System;
using System.Reflection;
using System.Text;
using Compilify.Extensions;
using Compilify.Models;
using Roslyn.Compilers;
using Roslyn.Compilers.CSharp;
using System.Collections.Generic;
using System.Linq;

namespace Compilify.Services
{
    public interface ICSharpCompilationProvider
    {
        Compilation Compile(Post post);
    }

    public class CSharpCompilationProvider : ICSharpCompilationProvider
    {
        private const string EntryPoint = 
            @"public class EntryPoint 
              {
                  public static object Result { get; set; }
                  
                  public static void Main()
                  {
                      Result = Script.Eval();
                  }
              }";

        private static readonly ReadOnlyArray<string> DefaultNamespaces =
            ReadOnlyArray<string>.CreateFrom(new[]
            {
                "System", 
                "System.IO", 
                "System.Net", 
                "System.Linq", 
                "System.Text", 
                "System.Text.RegularExpressions", 
                "System.Collections.Generic"
            });

        public Compilation Compile(Post post)
        {
            if (post == null)
            {
                throw new ArgumentNullException("post");
            }

            var console = SyntaxTree.ParseCompilationUnit("public static readonly StringWriter __Console = new StringWriter();", 
                                                          options: new ParseOptions(kind: SourceCodeKind.Script));

            var entry = SyntaxTree.ParseCompilationUnit(EntryPoint);

            var prompt = SyntaxTree.ParseCompilationUnit(BuildScript(post.Content), fileName: "Prompt",
                                                         options: new ParseOptions(kind: SourceCodeKind.Interactive))
                                   .RewriteWith<MissingSemicolonRewriter>();

            var editor = SyntaxTree.ParseCompilationUnit(post.Classes ?? string.Empty, fileName: "Editor", 
                                                         options: new ParseOptions(kind: SourceCodeKind.Script))
                                   .RewriteWith<MissingSemicolonRewriter>();

            var compilation =  Compile(post.Title ?? "Untitled", new[] { entry, prompt, editor, console });

            var newPrompt = prompt.RewriteWith(new ConsoleRewriter("__Console", compilation.GetSemanticModel(prompt)));
            var newEditor = editor.RewriteWith(new ConsoleRewriter("__Console", compilation.GetSemanticModel(editor)));

            return compilation.ReplaceSyntaxTree(prompt, newPrompt).ReplaceSyntaxTree(editor, newEditor);
        }

        public Compilation Compile(string compilationName, params SyntaxTree[] syntaxTrees)
        {
            if (string.IsNullOrEmpty(compilationName))
            {
                throw new ArgumentNullException("compilationName");
            }
            
            var options = new CompilationOptions(assemblyKind: AssemblyKind.ConsoleApplication, 
                                                 usings: DefaultNamespaces);

			var metadataReference = new List<MetadataReference>();
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(x => !x.IsDynamic && !string.IsNullOrEmpty(x.Location)))
			{
				metadataReference.Add(new AssemblyFileReference(assembly.Location));
			}

            var compilation = Compilation.Create(compilationName, options, syntaxTrees, metadataReference);

            return compilation;
        }

        private static string BuildScript(string content)
        {
            var builder = new StringBuilder();

            builder.AppendLine("public static object Eval() {");
            builder.AppendLine("#line 1");
            builder.Append(content);
            builder.AppendLine("}");

            return builder.ToString();
        }

    }
}
