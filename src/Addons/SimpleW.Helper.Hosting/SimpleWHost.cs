namespace SimpleW.Helper.Hosting {

    /// <summary>
    /// SimpleWHost
    /// </summary>
    public static class SimpleWHost {

        /// <summary>
        /// CreateApplicationBuilder
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static SimpleWHostApplicationBuilder CreateApplicationBuilder(string[] args) => new(args);

    }

}
