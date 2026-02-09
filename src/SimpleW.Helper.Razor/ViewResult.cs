using System.Dynamic;


namespace SimpleW.Helper.Razor {

    /// <summary>
    /// Returned by handlers/controllers to render a Razor view.
    /// </summary>
    public sealed class ViewResult {

        /// <summary>
        /// Name of the View
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Status Code of Response
        /// </summary>
        public int StatusCode { get; }

        /// <summary>
        /// Content-Type of Response
        /// </summary>
        public string ContentType { get; }

        /// <summary>
        /// Model passed to the View
        /// </summary>
        public object? Model { get; }

        /// <summary>
        /// ViewBag passed to the View
        /// </summary>
        public ExpandoObject ViewBag { get; } = new();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="name"></param>
        /// <param name="model"></param>
        /// <param name="statusCode"></param>
        /// <param name="contentType"></param>
        public ViewResult(string name, object? model = null, int statusCode = 200, string contentType = "text/html; charset=utf-8") {
            Name = name;
            Model = model;
            StatusCode = statusCode;
            ContentType = contentType;
        }

        /// <summary>
        /// Add a ViewBag
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public ViewResult WithViewBag(Action<dynamic> configure) {
            configure(ViewBag);
            return this;
        }

    }

}
