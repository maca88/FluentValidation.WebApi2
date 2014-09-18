// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http.Formatting;
using System.Runtime.CompilerServices;
using System.Web.Http.Controllers;
using System.Web.Http.Internal;
using System.Web.Http.Metadata;
using System.Web.Http.ModelBinding;

namespace System.Web.Http.Validation
{
    using FluentValidation.WebApi2;


    /// <summary>
    /// Recursively validate an object. 
    /// </summary>
    public class FluentValidationBodyModelValidator : IBodyModelValidator 
    {
        private static readonly MethodInfo getValidatorCache;
        private static readonly Type iModelValidatorCacheType;
        private static readonly MethodInfo getValidators;
        private static readonly PropertyInfo metadataRealModeType;

        static FluentValidationBodyModelValidator() {
            getValidatorCache = typeof(HttpActionContextExtensions).GetMethod("GetValidatorCache", BindingFlags.NonPublic | BindingFlags.Static);
            if(getValidatorCache == null)
                throw new NullReferenceException("Method GetValidatorCache was not found in HttpActionContextExtensions class");

            iModelValidatorCacheType = typeof(ApiController).Assembly.GetType("System.Web.Http.Validation.IModelValidatorCache");
            if (iModelValidatorCacheType == null)
                throw new NullReferenceException("Type System.Web.Http.Validation.IModelValidatorCache was not found in the System.Web.Http assembly");


            getValidators = typeof(HttpActionContextExtensions).GetMethod("GetValidators", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] {typeof(HttpActionContext), typeof(ModelMetadata), iModelValidatorCacheType}, null);
            if (getValidators == null)
                throw new NullReferenceException("Method GetValidators was not found in HttpActionContextExtensions class");

            metadataRealModeType = typeof(ModelMetadata).GetProperty("RealModelType", BindingFlags.NonPublic | BindingFlags.Instance);
            if (metadataRealModeType == null)
                throw new NullReferenceException("Internal property RealModelType was not found in ModelMetadata class");
        }

        private interface IKeyBuilder
        {
            string AppendTo(string prefix);
        }

        /// <summary>
        /// Determines whether the <paramref name="model"/> is valid and adds any validation errors to the <paramref name="actionContext"/>'s <see cref="ModelStateDictionary"/>
        /// </summary>
        /// <param name="model">The model to be validated.</param>
        /// <param name="type">The <see cref="Type"/> to use for validation.</param>
        /// <param name="metadataProvider">The <see cref="ModelMetadataProvider"/> used to provide the model metadata.</param>
        /// <param name="actionContext">The <see cref="HttpActionContext"/> within which the model is being validated.</param>
        /// <param name="keyPrefix">The <see cref="string"/> to append to the key for any validation errors.</param>
        /// <returns><c>true</c>if <paramref name="model"/> is valid, <c>false</c> otherwise.</returns>
        public bool Validate(object model, Type type, ModelMetadataProvider metadataProvider, HttpActionContext actionContext, string keyPrefix)
        {
            if (type == null)
            {
                throw new ArgumentException("type");
            }

            if (metadataProvider == null)
            {
                throw new ArgumentException("metadataProvider");
            }

            if (actionContext == null)
            {
                throw new ArgumentException("actionContext");
            }

            if (model != null && !ShouldValidateType(model.GetType()))
            {
                return true;
            }

            ModelValidatorProvider[] validatorProviders = actionContext.GetValidatorProviders().ToArray();
            // Optimization : avoid validating the object graph if there are no validator providers
            if (validatorProviders == null || validatorProviders.Length == 0)
            {
                return true;
            }

            ModelMetadata metadata = metadataProvider.GetMetadataForType(() => model, type);
            ValidationContext validationContext = new ValidationContext()
            {
                MetadataProvider = metadataProvider,
                ActionContext = actionContext,
                ValidatorCache = getValidatorCache.Invoke(null, new object[]{ actionContext }),
                ModelState = actionContext.ModelState,
                Visited = new HashSet<object>(ReferenceEqualityComparer.Instance),
                KeyBuilders = new Stack<IKeyBuilder>(),
                RootPrefix = keyPrefix
            };
            return ValidateNodeAndChildren(metadata, validationContext, container: null, validators: null);
        }

        /// <summary>
        /// Determines whether instances of a particular type should be validated
        /// </summary>
        /// <param name="type">The type to validate.</param>
        /// <returns><c>true</c> if the type should be validated; <c>false</c> otherwise</returns>
        public virtual bool ShouldValidateType(Type type)
        {
            return !MediaTypeFormatterCollection.IsTypeExcludedFromValidation(type);
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "See comment below")]
        private bool ValidateNodeAndChildren(ModelMetadata metadata, ValidationContext validationContext, object container, IEnumerable<ModelValidator> validators)
        {
            // Recursion guard to avoid stack overflows
            RuntimeHelpers.EnsureSufficientExecutionStack();

            object model = null;
            try
            {
                model = metadata.Model;
            }
            catch
            {
                // Retrieving the model failed - typically caused by a property getter throwing
                // Being unable to retrieve a property is not a validation error - many properties can only be retrieved if certain conditions are met
                // For example, Uri.AbsoluteUri throws for relative URIs but it shouldn't be considered a validation error
                return true;
            }

            bool isValid = true;

            if (validators == null)
            {
                validators = (IEnumerable<ModelValidator>)getValidators.Invoke(null, new object[] { validationContext.ActionContext, metadata, validationContext.ValidatorCache });//validationContext.ActionContext.GetValidators(metadata, validationContext.ValidatorCache);
            }

            // We don't need to recursively traverse the graph for null values
            if (model == null)
            {
                return ShallowValidate(metadata, validationContext, container, validators);
            }

            // We don't need to recursively traverse the graph for types that shouldn't be validated
            Type modelType = model.GetType();
            if (TypeHelper.IsSimpleType(modelType) || !ShouldValidateType(modelType))
            {
                return ShallowValidate(metadata, validationContext, container, validators);
            }

            // Check to avoid infinite recursion. This can happen with cycles in an object graph.
            if (validationContext.Visited.Contains(model))
            {
                return true;
            }
            validationContext.Visited.Add(model);

            // Validate the children first - depth-first traversal
            IEnumerable enumerableModel = model as IEnumerable;
            if (enumerableModel == null)
            {
                isValid = ValidateProperties(metadata, validationContext);
            }
            else
            {
                isValid = ValidateElements(enumerableModel, validationContext);
            }
            if (isValid)
            {
                // Don't bother to validate this node if children failed.
                isValid = ShallowValidate(metadata, validationContext, container, validators);
            }

            // Pop the object so that it can be validated again in a different path
            validationContext.Visited.Remove(model);

            return isValid;
        }

        private bool ValidateProperties(ModelMetadata metadata, ValidationContext validationContext)
        {
            bool isValid = true;
            PropertyScope propertyScope = new PropertyScope();
            validationContext.KeyBuilders.Push(propertyScope);
            foreach (ModelMetadata childMetadata in validationContext.MetadataProvider.GetMetadataForProperties(metadata.Model, metadataRealModeType.GetValue(metadata, null) as Type))
            {
                propertyScope.PropertyName = childMetadata.PropertyName;
                if (!ValidateNodeAndChildren(childMetadata, validationContext, metadata.Model, validators: null))
                {
                    isValid = false;
                }
            }
            validationContext.KeyBuilders.Pop();
            return isValid;
        }

        private bool ValidateElements(IEnumerable model, ValidationContext validationContext)
        {
            bool isValid = true;
            Type elementType = GetElementType(model.GetType());
            ModelMetadata elementMetadata = validationContext.MetadataProvider.GetMetadataForType(null, elementType);

            ElementScope elementScope = new ElementScope() { Index = 0 };
            validationContext.KeyBuilders.Push(elementScope);
            IEnumerable<ModelValidator> validators = (IEnumerable<ModelValidator>)getValidators.Invoke(null, new object[] {validationContext.ActionContext, elementMetadata, validationContext.ValidatorCache});

            // if there are no validators or the object is null we bail out quickly
            // when there are large arrays of null, this will save a significant amount of processing
            // with minimal impact to other scenarios.
            bool anyValidatorsDefined = validators.Any();

            foreach (object element in model)
            {
                // If the element is non null, the recursive calls might find more validators.
                // If it's null, then a shallow validation will be performed.
                if (element != null || anyValidatorsDefined)
                {
                    elementMetadata.Model = element;

                    if (!ValidateNodeAndChildren(elementMetadata, validationContext, model, validators))
                    {
                        isValid = false;
                    }
                }

                elementScope.Index++;
            }
            validationContext.KeyBuilders.Pop();
            return isValid;
        }

        // Validates a single node (not including children)
        // Returns true if validation passes successfully
        private static bool ShallowValidate(ModelMetadata metadata, ValidationContext validationContext, object container, IEnumerable<ModelValidator> validators)
        {
            bool isValid = true;
            string modelKey = null;

            Contract.Assert(validators != null);

            // When the are no validators we bail quickly. This saves a GetEnumerator allocation.
            // In a large array (tens of thousands or more) scenario it's very significant.
            ICollection validatorsAsCollection = validators as ICollection;
            if (validatorsAsCollection != null && validatorsAsCollection.Count == 0)
            {
                return isValid;
            }

            foreach (ModelValidator validator in validators)
            {
                // we use this flag to determine if we use the "patched" version or the default
                var fluentModelValidator = validator as IFluentModelValidator;

                var validationResults = fluentModelValidator != null 
                    ? fluentModelValidator.Validate(metadata, validationContext, container) 
                    : validator.Validate(metadata, container);

                foreach (ModelValidationResult error in validationResults)
                {
                    if (modelKey == null)
                    {
                        modelKey = validationContext.RootPrefix;
                        foreach (IKeyBuilder keyBuilder in validationContext.KeyBuilders.Reverse())
                        {
                            modelKey = keyBuilder.AppendTo(modelKey);
                        }

                        // Avoid adding model errors if the model state already contains model errors for that key
                        // We can't perform this check earlier because we compute the key string only when we detect an error
                        /*
                         The default condition is: !validationContext.ModelState.IsValidField(key)
                         For fluent validation we use validationContext.ModelState.ContainsKey(key)
                         This is to avoid missing errors in the ModelState in case the binder added errors before the validation
                        */
                        if ((fluentModelValidator == null && !validationContext.ModelState.IsValidField(modelKey)) ||
                            (fluentModelValidator != null && validationContext.ModelState.ContainsKey(modelKey)))
                        {
                            return false;
                        }
                    }
                    string errorKey = ModelBindingHelper.CreatePropertyModelName(modelKey, error.MemberName);
                    validationContext.ModelState.AddModelError(errorKey, error.Message);
                    isValid = false;
                }
            }
            return isValid;
        }

        private static Type GetElementType(Type type)
        {
            Contract.Assert(typeof(IEnumerable).IsAssignableFrom(type));
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            foreach (Type implementedInterface in type.GetInterfaces())
            {
                if (implementedInterface.IsGenericType && implementedInterface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return implementedInterface.GetGenericArguments()[0];
                }
            }

            return typeof(object);
        }

        private class PropertyScope : IKeyBuilder
        {
            public string PropertyName { get; set; }

            public string AppendTo(string prefix)
            {
                return ModelBindingHelper.CreatePropertyModelName(prefix, PropertyName);
            }
        }

        private class ElementScope : IKeyBuilder
        {
            public int Index { get; set; }

            public string AppendTo(string prefix)
            {
                return ModelBindingHelper.CreateIndexModelName(prefix, Index);
            }
        }

        
        private class ValidationContext : IValidationContext
        {
            public ModelMetadataProvider MetadataProvider { get; set; }
            public HttpActionContext ActionContext { get; set; }
            public dynamic ValidatorCache { get; set; }
            public ModelStateDictionary ModelState { get; set; }
            public HashSet<object> Visited { get; set; }
            public Stack<IKeyBuilder> KeyBuilders { get; set; }
            public string RootPrefix { get; set; }
        }
    }
}
