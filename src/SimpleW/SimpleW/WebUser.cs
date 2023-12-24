using System;
using System.Linq;


namespace SimpleW {


    /// <summary>
    /// IWebUser
    /// </summary>
    public interface IWebUser {

        /// <summary>
        /// true if user connected
        /// </summary>
        bool Identity { get; }

        /// <summary>
        /// The user id key
        /// </summary>
        Guid Id { get; }

        /// <summary>
        /// The user login
        /// </summary>
        string Login { get; }

        /// <summary>
        /// The user mail
        /// </summary>
        string Mail { get; }

        /// <summary>
        ///  The user fullname
        /// </summary>
        string FullName { get; }

        /// <summary>
        /// The user profile name
        /// </summary>
        string Profile { get; }

        /// <summary>
        /// The user roles : from profile and override
        /// </summary>
        string[] Roles { get; }

        /// <summary>
        /// The user json preferences
        /// </summary>
        string Preferences { get; }

        /// <summary>
        /// check if has roles
        /// </summary>
        /// <param name="roles">The roles to search</param>
        /// <returns></returns>
        bool IsInRoles(string roles);
        
        /// <summary>
        /// User to Return user properties
        /// </summary>
        /// <returns>object</returns>
        IWebUser Dump();

    }


    /// <summary>
    /// WebUser Class
    /// </summary>
    public class WebUser : IWebUser {

        /// <summary>
        /// true if user connected
        /// </summary>
        public virtual bool Identity { get; set; } = false;

        /// <summary>
        /// The user id key
        /// </summary>
        public virtual Guid Id { get; set; }

        /// <summary>
        /// The user login
        /// </summary>
        public virtual string Login { get; set; }

        /// <summary>
        /// The user mail
        /// </summary>
        public virtual string Mail { get; set; }

        /// <summary>
        ///  The user fullname
        /// </summary>
        public virtual string FullName { get; set; }

        /// <summary>
        /// The user profile name
        /// </summary>
        public virtual string Profile { get; set; }

        /// <summary>
        /// The user roles : from profile and override
        /// </summary>
        public virtual string[] Roles { get; set; }

        /// <summary>
        /// The user json preferences
        /// </summary>
        public virtual string Preferences { get; set; }

        /// <summary>
        /// check if has roles
        /// </summary>
        /// <param name="roles">The roles to search</param>
        /// <returns></returns>
        public bool IsInRoles(string roles) {
            if (Roles == null) {
                return false;
            }
            foreach (var role in roles.Split(',')) {
                if (Roles.Contains(role.Trim())) {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// User to Return user properties
        /// </summary>
        /// <returns>object</returns>
        public IWebUser Dump() {
            // do not remplace by return this
            // cause this class can be inherited and so
            // add other property of the underlying class
            // If the underlying class need to add property
            // it just need to override the Dump() method
            return new WebUser() {
                Identity = Identity,
                Id = Id,
                Login = Login,
                Mail = Mail,
                FullName = FullName,
                Profile = Profile,
                Roles = Roles,
                Preferences = Preferences
            };
        }

    }


    /// <summary>
    /// TokenWebUser Class
    /// use to map IWebUser/JwtSecurityToken
    /// </summary>
    public class TokenWebUser : WebUser {

        /// <summary>
        /// The JWT encoded token string
        /// </summary>
        public string Token;

        /// <summary>
        /// Flag to indicate backend must refres
        /// the webuser (role, mail...)
        /// </summary>
        public bool Refresh = true;

        /// <summary>
        /// Constructor
        /// </summary>
        public TokenWebUser() {
        }

        /// <summary>
        /// Constructor from IWebUser
        /// </summary>
        /// <param name="webuser"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public TokenWebUser(IWebUser webuser) {
            if (webuser == null) {
                throw new ArgumentNullException();
            }
            Identity = webuser.Identity;
            Id = webuser.Id;
            Login = webuser.Login;
            Mail = webuser.Mail;
            FullName = webuser.FullName;
            Profile = webuser.Profile;
            Roles = webuser.Roles;
            Preferences = webuser.Preferences;
        }

        /// <summary>
        /// Set Token and call Dump()
        /// </summary>
        /// <param name="jwt"></param>
        /// <returns></returns>
        public IWebUser Dump(string jwt) {
            Token = jwt;
            return this;
        }

    }


}
