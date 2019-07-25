// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.NetCore.Analyzers.Runtime
{
    /// <summary>
    /// CA2216: Disposable types should declare finalizer
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic), Shared]
    public sealed class DisposableTypesShouldDeclareFinalizerAnalyzer : DiagnosticAnalyzer
    {
        internal const string RuleId = "CA2216";

        private static readonly LocalizableString s_localizableTitle = new LocalizableResourceString(nameof(MicrosoftNetCoreAnalyzersResources.DisposableTypesShouldDeclareFinalizerTitle), MicrosoftNetCoreAnalyzersResources.ResourceManager, typeof(MicrosoftNetCoreAnalyzersResources));

        private static readonly LocalizableString s_localizableMessage = new LocalizableResourceString(nameof(MicrosoftNetCoreAnalyzersResources.DisposableTypesShouldDeclareFinalizerMessage), MicrosoftNetCoreAnalyzersResources.ResourceManager, typeof(MicrosoftNetCoreAnalyzersResources));
        private static readonly LocalizableString s_localizableDescription = new LocalizableResourceString(nameof(MicrosoftNetCoreAnalyzersResources.DisposableTypesShouldDeclareFinalizerDescription), MicrosoftNetCoreAnalyzersResources.ResourceManager, typeof(MicrosoftNetCoreAnalyzersResources));

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(RuleId,
                                                                             s_localizableTitle,
                                                                             s_localizableMessage,
                                                                             DiagnosticCategory.Usage,
                                                                             DiagnosticHelpers.DefaultDiagnosticSeverity,
                                                                             isEnabledByDefault: DiagnosticHelpers.EnabledByDefaultIfNotBuildingVSIX,
                                                                             description: s_localizableDescription,
                                                                             helpLinkUri: "https://docs.microsoft.com/visualstudio/code-quality/ca2216-disposable-types-should-declare-finalizer",
                                                                             customTags: FxCopWellKnownDiagnosticTags.PortedFxCopRule);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticHelpers.EnabledByDefaultIfNotBuildingVSIX ? ImmutableArray.Create(Rule) : ImmutableArray<DiagnosticDescriptor>.Empty;

        public override void Initialize(AnalysisContext analysisContext)
        {
            analysisContext.EnableConcurrentExecution();
            analysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

            analysisContext.RegisterCompilationStartAction(
                compilationStartAnalysisContext =>
                {
                    Compilation compilation = compilationStartAnalysisContext.Compilation;

                    ImmutableHashSet<INamedTypeSymbol> nativeResourceTypes = ImmutableHashSet.Create(
                        WellKnownTypes.IntPtr(compilation),
                        WellKnownTypes.UIntPtr(compilation),
                        WellKnownTypes.HandleRef(compilation)
                    );
                    var disposableType = WellKnownTypes.IDisposable(compilation);

                    compilationStartAnalysisContext.RegisterOperationAction(
                        operationAnalysisContext =>
                        {
                            var assignment = (IAssignmentOperation)operationAnalysisContext.Operation;

                            IOperation target = assignment.Target;
                            if (target == null)
                            {
                                // This can happen if the left-hand side is an undefined symbol.
                                return;
                            }

                            if (target.Kind != OperationKind.FieldReference)
                            {
                                return;
                            }

                            var fieldReference = (IFieldReferenceOperation)target;
                            if (!(fieldReference.Member is IFieldSymbol field) || field.Kind != SymbolKind.Field || field.IsStatic)
                            {
                                return;
                            }

                            if (!nativeResourceTypes.Contains(field.Type))
                            {
                                return;
                            }

                            INamedTypeSymbol containingType = field.ContainingType;
                            if (containingType == null || containingType.IsValueType)
                            {
                                return;
                            }

                            if (!containingType.AllInterfaces.Contains(disposableType))
                            {
                                return;
                            }

                            if (containingType.HasFinalizer())
                            {
                                return;
                            }

                            if (assignment.Value == null || assignment.Value.Kind != OperationKind.Invocation)
                            {
                                return;
                            }

                            var invocation = (IInvocationOperation)assignment.Value;
                            if (invocation == null)
                            {
                                return;
                            }

                            IMethodSymbol method = invocation.TargetMethod;

                            // TODO: What about COM?
                            if (method.GetDllImportData() == null)
                            {
                                return;
                            }

                            operationAnalysisContext.ReportDiagnostic(containingType.CreateDiagnostic(Rule));
                        },
                        OperationKind.SimpleAssignment);
                });
        }
    }
}