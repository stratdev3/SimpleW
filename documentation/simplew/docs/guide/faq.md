# Frequently Asked Questions


> Is this a toy or production-ready ?

I've been working in web application development for the past 20 years. SimpleW is the result of experiments, successes, failures, and lessons learned. It covers my own needs, and I've been using it in all my SaaS products for the last 5 years.
All core components are production-ready.

> Why open-source this tool ?

Because I strongly believe in open source, and I use open-source software every day. It's simply a matter of principle.

> Why do I dislike ASP.NET Core ?

I don't hate it, but I don't like it (see the paragraph about [Why this library exists](./what-is-simplew.md#why-this-library-)).

> What is the primary goal of SimpleW ?

It's in the name : **simplicity first**.

> "Blazingly Fast" slogan ?

I admit that nowadays almost every open-source project overuses the word blazing in its description.
But when it is actually true (see [performance comparisons](./performances.md) or [TechEmpower](https://www.techempower.com/benchmarks) results), then it deserves to be used.

> How can I add custom headers to all responses ?

Depending on how you use handlers, you can insert your "always-on" headers either :
- In a custom `ResultHandler`
- Or by declaring a new middleware

The first approach is more efficient and performant, but the second one covers all cases.

> Support .NET Framework

I don't want to support .NET Framework on Windows.
Just update your dying runtime !

> Addons ?

I want to keep the core simple and lightweight so that :
- The code can be reviewed by a human in a few hours
- Or fully ingested (and reasoned about) by an AI in a few seconds

Addons are the place for everything that does not belong in the core.
Also, a product is much easier to adopt when there is a healthy ecosystem around it.
