using SimpleW;
using SimpleW.Helper.Razor;


namespace test {

    /// <summary>
    /// Home Controller
    /// </summary>
    [Route("/home")]
    public class HomeController : RazorController {

        /// <summary>
        /// Index
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        [Route("GET", "/index")]
        public object Index(string? name = null) {
            var model = new { name };
            return View("Home/Index", model).WithViewBag(vb => {
                vb.Title = "Home";
                vb.Footer = "SimpleW";
            });
        }

        /// <summary>
        /// About
        /// </summary>
        /// <returns></returns>
        [Route("GET", "/about")]
        public object About() {
            var model = new { Title = "About" };
            return View("Home/About", model).WithViewBag(vb => {
                vb.Title = "About";
                vb.Footer = "SimpleW";
            });
        }

    }

}
