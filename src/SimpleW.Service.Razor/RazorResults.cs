namespace SimpleW.Service.Razor {

    /// <summary>
    /// RazorResults
    /// </summary>
    public static class RazorResults {

        /// <summary>
        /// Return a Razor View
        /// </summary>
        /// <param name="name"></param>
        /// <param name="model"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        public static ViewResult View(string name, object? model = null, int statusCode = 200) => new(name, model, statusCode);

    }

}
