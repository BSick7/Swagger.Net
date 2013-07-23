﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Web;
using System.Web.Http.Controllers;
using System.Web.Http.Description;
using Newtonsoft.Json;

namespace Swagger.Net
{
    public static class SwaggerGen
    {
        public const string SWAGGER = "swagger";
        public const string SWAGGER_VERSION = "2.0";
        public const string FROMURI = "FromUri";
        public const string FROMBODY = "FromBody";
        public const string QUERY = "query";
        public const string PATH = "path";
        public const string BODY = "body";

        /// <summary>
        /// Create a resource listing
        /// </summary>
        /// <param name="actionContext">Current action context</param>
        /// <param name="includeResourcePath">Should the resource path property be included in the response</param>
        /// <returns>A resource Listing</returns>
        public static ResourceListing CreateResourceListing(HttpActionContext actionContext, bool includeResourcePath = true)
        {
            return CreateResourceListing(actionContext.ControllerContext, includeResourcePath);
        }

        /// <summary>
        /// Create a resource listing
        /// </summary>
        /// <param name="actionContext">Current controller context</param>
        /// <param name="includeResourcePath">Should the resource path property be included in the response</param>
        /// <returns>A resrouce listing</returns>
        public static ResourceListing CreateResourceListing(HttpControllerContext controllerContext, bool includeResourcePath = false)
        {
            Uri uri = controllerContext.Request.RequestUri;

            ResourceListing rl = new ResourceListing()
            {
                apiVersion = Assembly.GetCallingAssembly().GetType().Assembly.GetName().Version.ToString(),
                swaggerVersion = SWAGGER_VERSION,
                basePath = uri.GetLeftPart(UriPartial.Authority) + HttpRuntime.AppDomainAppVirtualPath.TrimEnd('/'),
                apis = new List<ResourceApi>(),
                models = new Dictionary<string, ResourceModel>()
            };

            if (includeResourcePath) rl.resourcePath = controllerContext.ControllerDescriptor.ControllerName;

            return rl;
        }

        /// <summary>
        /// Create an api element. swagger root defines if request is to load all apis with in a controller or request an api
        /// </summary>
        /// <param name="api">Description of the api via the ApiExplorer</param>
        /// <param name="swaggerRoot">if set to <c>true</c> [swagger root].</param>
        /// <returns>
        /// A resource api
        /// </returns>
        public static ResourceApi CreateResourceApi(ApiDescription api, bool swaggerRoot)
        {
            var apiRelPath = api.RelativePath;
            if (swaggerRoot)
            {
                int queryIndex = apiRelPath.IndexOf('?');
                if (queryIndex > 0)
                {
                    apiRelPath = apiRelPath.Substring(0, queryIndex);
                }
            }
            ResourceApi rApi = new ResourceApi()
            {
                path = "/" + apiRelPath,
                description = api.Documentation,
                operations = new List<ResourceApiOperation>()
            };

            return rApi;
        }

        /// <summary>
        /// Creates the resource model.
        /// </summary>
        /// <param name="api">Description of the api via the ApiExplorer.</param>
        /// <returns>A resource model</returns>
        public static List<ResourceModel> CreateResourceModel(Type returnType)
        {

            if (returnType == null)
                return null;
            if (returnType.IsGenericType)
                returnType = returnType.GetGenericArguments()[0];
            if (returnType.IsArray)
                returnType = returnType.GetElementType();
            if (IsSystemType(returnType)) return null;

            List<ResourceModel> modelArray = new List<ResourceModel>();
            ResourceModel model = new ResourceModel()
            {
                id = returnType.Name,
                name = returnType.Name,
                NameSpace = returnType.ToString(),
                properties = new Dictionary<string, ResourceModelProperty>()
            };

            UpdateModelWithProperties(returnType, modelArray, model);
            modelArray.Add(model);
            return modelArray;
        }

        /// <summary>
        /// Updates the model with properties.
        /// </summary>
        /// <param name="returnType">Type of the return.</param>
        /// <param name="modelArray">The model array.</param>
        /// <param name="model">The model.</param>
        private static void UpdateModelWithProperties(Type returnType, List<ResourceModel> modelArray, ResourceModel model)
        {
            var properties = returnType.GetProperties();

            foreach (var property in properties)
            {
                ResourceModelProperty prop = new ResourceModelProperty();
                prop.required = property.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), true).Any();
                string propType=GetPropertyTypeAsString(property.PropertyType);
                if (property.PropertyType.IsGenericType)
                {
                    prop.type = "List";
                    prop.items = new Dictionary<string,string>();
                    prop.items.Add("$ref", propType);
                }
                else
                {
                    prop.type = propType;
                }
                model.properties.Add(property.Name, prop);

                if (!IsSystemType(property.PropertyType))
                {
                    var innerModel = CreateResourceModel(property.PropertyType);
                    if (innerModel != null)
                    {
                        modelArray.AddRange(innerModel);
                    }
                }
            }
        }

        /// <summary>
        /// Creates an api operation
        /// </summary>
        /// <param name="api">Description of the api via the ApiExplorer</param>
        /// <param name="docProvider">Access to the XML docs written in code</param>
        /// <returns>An api operation</returns>
        public static ResourceApiOperation CreateResourceApiOperation(ApiDescription api, XmlCommentDocumentationProvider docProvider)
        {
            ResourceApiOperation rApiOperation = new ResourceApiOperation()
            {
                httpMethod = api.HttpMethod.ToString(),
                nickname = docProvider.GetNickname(api.ActionDescriptor),
                responseClass = docProvider.GetResponseClass(api.ActionDescriptor),
                summary = api.Documentation,
                notes = docProvider.GetNotes(api.ActionDescriptor),
                parameters = new List<ResourceApiOperationParameter>()
            };

            return rApiOperation;
        }

        /// <summary>
        /// Creates an operation parameter
        /// </summary>
        /// <param name="api">Description of the api via the ApiExplorer</param>
        /// <param name="param">Description of a parameter on an operation via the ApiExplorer</param>
        /// <param name="docProvider">Access to the XML docs written in code</param>
        /// <returns>An operation parameter</returns>
        public static ResourceApiOperationParameter CreateResourceApiOperationParameter(ApiDescription api, ApiParameterDescription param, XmlCommentDocumentationProvider docProvider)
        {
            string paramType = (param.Source.ToString().Equals(FROMURI)) ? QUERY : BODY;
            ResourceApiOperationParameter parameter = new ResourceApiOperationParameter()
            {
                paramType = (paramType == "query" && api.RelativePath.IndexOf("{" + param.Name + "}") > -1) ? PATH : paramType,
                name = param.Name,
                description = param.Documentation,
                dataType = param.ParameterDescriptor.ParameterType.Name,
                required = docProvider.GetRequired(param.ParameterDescriptor)
            };

            return parameter;
        }

        /// <summary>
        /// Gets the type as string.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns></returns>
        public static string GetTypeAsString(Type type)
        {
            if (type.IsGenericType)
            {
                //Swagger api container type supports List, Set, Array
                //https://github.com/wordnik/swagger-core/wiki/Datatypes#containers
                StringBuilder sb = new StringBuilder("List");
                sb.Append("[");
                Type[] types = type.GetGenericArguments();
                for (int i = 0; i < types.Length; i++)
                {
                    sb.Append(GetTypeAsString(types[i]));
                    if (i != (types.Length - 1)) sb.Append(", ");
                }
                sb.Append("]");
                return sb.ToString();
            }
            else
                return type.Name;
        }


        /// <summary>
        /// Tries the get model from API aware.
        /// </summary>
        /// <param name="httpActionDescriptor">The HTTP action descriptor.</param>
        /// <param name="rModels">The r models.</param>
        /// <returns></returns>
        /// <exception cref="System.NotImplementedException"></exception>
        internal static bool TryGetModelFromApiAware(HttpActionDescriptor httpActionDescriptor, out List<ResourceModel> rModels)
        {
            bool modelFound = false;
            rModels = new List<ResourceModel>();
            var models = new List<ResourceModel>();
          
            var ctrlAttributes = httpActionDescriptor.ControllerDescriptor.GetCustomAttributes<ApiAwareAttribute>();
            if (ctrlAttributes != null & ctrlAttributes.Any())
            {
                if (ctrlAttributes.First().ModelType != null)
                {
                    models = CreateResourceModel(ctrlAttributes.First().ModelType);
                    modelFound = true;
                }
            }
            var attributes = httpActionDescriptor.GetCustomAttributes<ApiAwareAttribute>();
            if (attributes != null && attributes.Any())
            {
                if (attributes.First().ModelType != null)
                {
                    models.AddRange(CreateResourceModel(attributes.First().ModelType));
                    modelFound = true;
                }
            }
            if (models.Any())
            {
                rModels = models.Distinct(new ResourceModelCompararor()).ToList();
            }
            return modelFound;
        }


        /// <summary>
        /// Determines whether [is system type] [the specified type].
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>
        ///   <c>true</c> if [is system type] [the specified type]; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsSystemType(Type type)
        {
            if (!type.IsGenericType && (type.IsPrimitive || type.Namespace.StartsWith("System") ||
                type.Module.ScopeName.Equals("CommonLanguageRuntimeLibrary")))
                return true;
            return false;
        }

        /// <summary>
        /// Gets the property type as string.
        /// </summary>
        /// <param name="propertyType">Type of the property.</param>
        /// <returns></returns>
        private static string GetPropertyTypeAsString(Type propertyType)
        {
            if (propertyType.IsGenericType)
            {
                Type firstargumentType = propertyType.GetGenericArguments()[0];
                return GetPropertyTypeAsString(firstargumentType);
            }
            else if (propertyType.IsArray)
                return propertyType.GetElementType().Name;

            return propertyType.Name;
        }

        internal static bool TryGetReturnModelFromApiAware(ReflectedHttpActionDescriptor reflectedActionDescriptor, ref string responseClassName)
        {
            var attributes = reflectedActionDescriptor.GetCustomAttributes<ApiAwareAttribute>();
            if (attributes != null && attributes.Any())
            {
                var attr = attributes.First();
                responseClassName= GetPropertyTypeAsString(attr.ModelType);
                return true;
            }
            return false;
        }
    }

    public class ResourceListing
    {
        public string apiVersion { get; set; }
        public string swaggerVersion { get; set; }
        public string basePath { get; set; }
        public string resourcePath { get; set; }
        public List<ResourceApi> apis { get; set; }
        public Dictionary<string, ResourceModel> models { get; set; }
    }

    public class ResourceApi
    {
        public string path { get; set; }
        public string description { get; set; }
        public List<ResourceApiOperation> operations { get; set; }
    }

    public class ResourceApiOperation
    {
        public string httpMethod { get; set; }
        public string nickname { get; set; }
        public string responseClass { get; set; }
        public string summary { get; set; }
        public string notes { get; set; }
        public string[] produces { get { return new string[] { "application/json", "application/xml" }; } }
        public List<ResourceApiOperationParameter> parameters { get; set; }
    }

    public class ResourceApiOperationParameter
    {
        public string paramType { get; set; }
        public string name { get; set; }
        public string description { get; set; }
        public string dataType { get; set; }
        public bool required { get; set; }
        public bool allowMultiple { get; set; }
        public OperationParameterAllowableValues allowableValues { get; set; }
    }

    public class OperationParameterAllowableValues
    {
        public int max { get; set; }
        public int min { get; set; }
        public string valueType { get; set; }
    }

    public class ResourceModel
    {
        public string id { get; set; }
        public string name { get; set; }
        [JsonIgnore]
        [IgnoreDataMember]
        public string NameSpace { get; set; }
        public Dictionary<string, ResourceModelProperty> properties { get; set; }
    }

    public class ResourceModelProperty
    {
        public string type { get; set; }
        public bool required { get; set; }

        [DefaultValue(null)]
        public Dictionary<string,string> items { get; set; }
    }

    /// <summary>
    /// ResourceModel Compararor
    /// </summary>
    public class ResourceModelCompararor : IEqualityComparer<ResourceModel>
    {
        /// <summary>
        /// Compares the ResourceModel.
        /// </summary>
        /// <param name="x">The x.</param>
        /// <param name="y">The y.</param>
        /// <returns></returns>
        public bool Equals(ResourceModel x, ResourceModel y)
        {
            return x.NameSpace.Equals(y.NameSpace, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <param name="obj">The obj.</param>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public int GetHashCode(ResourceModel obj)
        {
            return obj.NameSpace.Length;
        }
    }

}