# IWebUser [⚠️ need update to v26]

The interface is both implemented by `WebUser` and `TokenWebUser`.
It is used to set the `webuser` property in `Controller`.


## Identity

```csharp
/// <summary>
/// true if user connected
/// </summary>
bool Identity { get; }
```


## Id

```csharp
        /// <summary>
        /// The user id key
        /// </summary>
        Guid Id { get; }
```


## Login

```csharp
        /// <summary>
        /// The user login
        /// </summary>
        string Login { get; }
```


## Mail

```csharp
/// <summary>
/// The user mail
/// </summary>
string Mail { get; }
```


## FullName

```csharp
/// <summary>
///  The user fullname
/// </summary>
string FullName { get; }
```

## Profile

```csharp
/// <summary>
/// The user profile name
/// </summary>
string Profile { get; }
```

## Roles

```csharp
/// <summary>
/// The user roles : from profile and override
/// </summary>
string[] Roles { get; }
```


## Preferences

```csharp
/// <summary>
/// The user json preferences
/// </summary>
string Preferences { get; }
```


## IsInRoles

```csharp
/// <summary>
/// check if has roles
/// </summary>
/// <param name="roles">The roles to search</param>
/// <returns></returns>
bool IsInRoles(string roles);
```


## Dump

```csharp
/// <summary>
/// User to Return user properties
/// </summary>
/// <returns>object</returns>
IWebUser Dump();
```
