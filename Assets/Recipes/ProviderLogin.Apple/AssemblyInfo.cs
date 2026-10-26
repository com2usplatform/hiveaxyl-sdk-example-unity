// Copyright (c) Com2uS Platform Corp. All rights reserved.

using System.Runtime.CompilerServices;

// Share internal helpers with the assemblies listed below without expanding the public API.
[assembly: InternalsVisibleTo("Hive.Axyl.Samples.Recipes.Tests")]

// The QA Explorer exercises authorization without exchanging the code.
[assembly: InternalsVisibleTo("Hive.Axyl.Samples.QA.Explorer")]
