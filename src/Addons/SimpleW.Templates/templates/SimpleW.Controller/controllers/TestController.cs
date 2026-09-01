using SimpleW;


namespace test {

    /// <summary>
    /// Test Controller
    /// </summary>
    [Route("/test")]
    public class TestController : Controller {

        /// <summary>
        /// Hello World
        /// </summary>
        [Route("GET", "/hello")]
        public object Hello(string? name = null) {
            return new { message = $"{name}, Hello World !" };
        }

    }

}
