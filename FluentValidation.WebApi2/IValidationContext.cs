using System.Web.Http.Controllers;
using System.Web.Http.Metadata;
using System.Web.Http.ModelBinding;

namespace FluentValidation.WebApi2
{
    public interface IValidationContext
    {
        ModelMetadataProvider MetadataProvider { get; set; }

        HttpActionContext ActionContext { get; set; }

        ModelStateDictionary ModelState { get; set; }

        string RootPrefix { get; set; }
    }
}
