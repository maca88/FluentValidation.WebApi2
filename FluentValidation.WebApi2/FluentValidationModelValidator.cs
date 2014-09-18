#region License
// Copyright (c) Jeremy Skinner (http://www.jeremyskinner.co.uk)
// 
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
// 
// http://www.apache.org/licenses/LICENSE-2.0 
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
// 
// The latest version of this file can be found at http://www.codeplex.com/FluentValidation
#endregion

namespace FluentValidation.WebApi2
{
	using System.Collections.Generic;
	using System.Linq;
	using System.Web.Http.Metadata;
	using System.Web.Http.Validation;
	using Internal;
	using Results;

    public class FluentValidationModelValidator : ModelValidator, IFluentModelValidator
    {
		readonly IValidator validator;

		public FluentValidationModelValidator(IEnumerable<ModelValidatorProvider> validatorProviders, IValidator validator)
			: base(validatorProviders) {
			this.validator = validator;
		}


        public IEnumerable<ModelValidationResult> Validate(ModelMetadata metadata, IValidationContext validationContext, object container)
        {
            if (metadata.Model == null) return Enumerable.Empty<ModelValidationResult>();

            CustomizeValidatorAttribute validatorAttribute = null;

            foreach (var param in validationContext.ActionContext.ActionDescriptor.GetParameters()
                .Where(p => p.ParameterType == metadata.ModelType)) {
                validatorAttribute = param.GetCustomAttributes<CustomizeValidatorAttribute>().FirstOrDefault();
                if (validatorAttribute != null) break;
            }

            if(validatorAttribute == null)
                validatorAttribute = new CustomizeValidatorAttribute();

            var selector = validatorAttribute.ToValidatorSelector();
            var interceptor = validatorAttribute.GetInterceptor();
            
            var context = new ValidationContext(metadata.Model, new PropertyChain(), selector);

            if (interceptor != null)
            {
                // Allow the user to provide a customized context
                // However, if they return null then just use the original context.
                context = interceptor.BeforeWebApiValidation(validationContext.ActionContext, metadata, context) ?? context;
            }

            var result = validator.Validate(context);

            if (interceptor != null)
            {
                // allow the user to provice a custom collection of failures, which could be empty.
                // However, if they return null then use the original collection of failures. 
                result = interceptor.AfterWebApiValidation(validationContext.ActionContext, metadata, context, result) ?? result;
            }

            return !result.IsValid 
                ? ConvertValidationResultToModelValidationResults(result) 
                : Enumerable.Empty<ModelValidationResult>();
        }

		public override IEnumerable<ModelValidationResult> Validate(ModelMetadata metadata, object container) {
		    if (metadata.Model == null) return Enumerable.Empty<ModelValidationResult>();
		    var selector = new DefaultValidatorSelector();
		    var context = new ValidationContext(metadata.Model, new PropertyChain(), selector);

		    var result = validator.Validate(context);

		    return !result.IsValid 
                ? ConvertValidationResultToModelValidationResults(result) 
                : Enumerable.Empty<ModelValidationResult>();
		}

		protected virtual IEnumerable<ModelValidationResult> ConvertValidationResultToModelValidationResults(ValidationResult result) {
			return result.Errors.Select(x => new ModelValidationResult
			{
				MemberName = x.PropertyName,
				Message = x.ErrorMessage
			});
		}

        
    }
}