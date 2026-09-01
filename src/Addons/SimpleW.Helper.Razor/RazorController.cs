namespace SimpleW.Helper.Razor {

    /// <summary>
    /// Base controller that provides View().
    /// </summary>
    public abstract class RazorController : Controller {

        /// <summary>
        /// Return a Razor View
        /// </summary>
        /// <param name="name"></param>
        /// <param name="model"></param>
        /// <param name="statusCode"></param>
        /// <returns></returns>
        protected ViewResult View(string name, object? model = null, int statusCode = 200) => new(name, model, statusCode);

    }
}
