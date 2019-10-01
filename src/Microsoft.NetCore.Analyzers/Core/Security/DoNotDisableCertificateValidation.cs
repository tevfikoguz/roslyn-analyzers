// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.NetCore.Analyzers.Security
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public sealed class DoNotDisableCertificateValidation : DiagnosticAnalyzer
    {
        internal const string DiagnosticId = "CA5359";
        private static readonly LocalizableString s_Title = new LocalizableResourceString(
            nameof(MicrosoftNetCoreAnalyzersResources.DoNotDisableCertificateValidation),
            MicrosoftNetCoreAnalyzersResources.ResourceManager,
            typeof(MicrosoftNetCoreAnalyzersResources));
        private static readonly LocalizableString s_Message = new LocalizableResourceString(
            nameof(MicrosoftNetCoreAnalyzersResources.DoNotDisableCertificateValidationMessage),
            MicrosoftNetCoreAnalyzersResources.ResourceManager,
            typeof(MicrosoftNetCoreAnalyzersResources));
        private static readonly LocalizableString s_Description = new LocalizableResourceString(
            nameof(MicrosoftNetCoreAnalyzersResources.DoNotDisableCertificateValidationDescription),
            MicrosoftNetCoreAnalyzersResources.ResourceManager,
            typeof(MicrosoftNetCoreAnalyzersResources));

        internal static DiagnosticDescriptor Rule = new DiagnosticDescriptor(
                DiagnosticId,
                s_Title,
                s_Message,
                DiagnosticCategory.Security,
                DiagnosticHelpers.DefaultDiagnosticSeverity,
                isEnabledByDefault: DiagnosticHelpers.EnabledByDefaultIfNotBuildingVSIX,
                description: s_Description,
                helpLinkUri: null,
                customTags: WellKnownDiagnosticTags.Telemetry);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public sealed override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();

            // Security analyzer - analyze and report diagnostics on generated code.
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

            context.RegisterCompilationStartAction(
                (CompilationStartAnalysisContext compilationStartAnalysisContext) =>
                {
                    var compilation = compilationStartAnalysisContext.Compilation;
                    var systemNetSecurityRemoteCertificateValidationCallbackTypeSymbol = compilation.GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemNetSecurityRemoteCertificateValidationCallback);
                    var obj = WellKnownTypeProvider.GetOrCreate(compilation).SystemObject;
                    var x509Certificate = compilation.GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemSecurityCryptographyX509CertificatesX509Certificate);
                    var x509Chain = compilation.GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemSecurityCryptographyX509CertificatesX509Chain);
                    var sslPolicyErrors = compilation.GetOrCreateTypeByMetadataName(WellKnownTypeNames.SystemNetSecuritySslPolicyErrors);

                    if (systemNetSecurityRemoteCertificateValidationCallbackTypeSymbol == null
                        || obj == null
                        || x509Certificate == null
                        || x509Chain == null
                        || sslPolicyErrors == null)
                    {
                        return;
                    }

                    compilationStartAnalysisContext.RegisterOperationAction(
                        (OperationAnalysisContext operationAnalysisContext) =>
                        {
                            var delegateCreationOperation =
                                (IDelegateCreationOperation)operationAnalysisContext.Operation;

                            if (systemNetSecurityRemoteCertificateValidationCallbackTypeSymbol.Equals(delegateCreationOperation.Type))
                            {
                                var alwaysReturnTrue = false;

                                switch (delegateCreationOperation.Target.Kind)
                                {
                                    case OperationKind.AnonymousFunction:
                                        if (!IsCertificateValidationFunction(
                                            (delegateCreationOperation.Target as IAnonymousFunctionOperation).Symbol,
                                            obj,
                                            x509Certificate,
                                            x509Chain,
                                            sslPolicyErrors))
                                        {
                                            return;
                                        }

                                        alwaysReturnTrue = AlwaysReturnTrue(delegateCreationOperation.Target.Descendants());
                                        break;

                                    case OperationKind.MethodReference:
                                        var methodReferenceOperation = (IMethodReferenceOperation)delegateCreationOperation.Target;
                                        var methodSymbol = methodReferenceOperation.Method;

                                        if (!IsCertificateValidationFunction(
                                            methodSymbol,
                                            obj,
                                            x509Certificate,
                                            x509Chain,
                                            sslPolicyErrors))
                                        {
                                            return;
                                        }

                                        var blockOperation = methodSymbol.GetTopmostOperationBlock(compilation);

                                        if (blockOperation == null)
                                        {
                                            return;
                                        }

                                        // TODO(LINCHE): This is an issue tracked by #2009. We filter extraneous based on IsImplicit.
                                        var targetOperations = ImmutableArray.ToImmutableArray(blockOperation.Descendants()).WithoutFullyImplicitOperations();
                                        alwaysReturnTrue = AlwaysReturnTrue(targetOperations);
                                        break;
                                }

                                if (alwaysReturnTrue)
                                {
                                    operationAnalysisContext.ReportDiagnostic(
                                        delegateCreationOperation.CreateDiagnostic(
                                            Rule));
                                }
                            }
                        },
                        OperationKind.DelegateCreation);
                });
        }

        private static bool IsCertificateValidationFunction(IMethodSymbol methodSymbol, INamedTypeSymbol obj, INamedTypeSymbol x509Certificate, INamedTypeSymbol x509Chain, INamedTypeSymbol sslPolicyErrors)
        {
            if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Boolean)
            {
                return false;
            }

            var parameters = methodSymbol.Parameters;

            if (parameters.Length != 4)
            {
                return false;
            }

            if (!parameters[0].Type.Equals(obj)
                || !parameters[1].Type.Equals(x509Certificate)
                || !parameters[2].Type.Equals(x509Chain)
                || !parameters[3].Type.Equals(sslPolicyErrors))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Find every IReturnOperation in the method and get the value of return statement to determine if the method always return true.
        /// </summary>
        /// <param name="operations">A method body in the form of explicit IOperations</param>
        private static bool AlwaysReturnTrue(IEnumerable<IOperation> operations)
        {
            var hasReturnStatement = false;

            foreach (var descendant in operations)
            {
                if (descendant.Kind == OperationKind.Return)
                {
                    var returnOperation = (IReturnOperation)descendant;

                    if (returnOperation.ReturnedValue == null)
                    {
                        return false;
                    }

                    var constantValue = returnOperation.ReturnedValue.ConstantValue;
                    hasReturnStatement = true;

                    // Check if the value being returned is a compile time constant 'true'
                    if (!constantValue.HasValue || constantValue.Value.Equals(false))
                    {
                        return false;
                    }
                }
            }

            return hasReturnStatement;
        }
    }
}
