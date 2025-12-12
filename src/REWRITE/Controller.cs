namespace SimpleW {

    /// <summary>
    /// Controller is mandatory base class of all Controllers
    /// Inherit from this class or subclass
    /// </summary>
    public abstract class Controller {

        /// <summary>
        /// Gets the current HttpSession
        /// </summary>
        public HttpSession Session { get; internal set; } = default!;

        /// <summary>
        /// Gets the current HttpRequest.
        /// </summary>
        public HttpRequest Request => Session.Request;

    }

}