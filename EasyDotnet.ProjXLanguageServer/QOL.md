Hot — daily wins                                                                          
  1. Generate UserSecretsId — empty/missing → insert
  <UserSecretsId>{newGuid}</UserSecretsId> and create the secrets dir on disk. Pairs with   
  the GUID hover/go-to-def already in place.                                             
  3. Bump PackageReference version to latest — query NuGet, offer "Update X to " / "Update X
   to ". Can be invoked on a single ref or "Update all packages in this project".           
  4. Remove unused / dangling <ProjectReference> — when the path doesn't resolve on disk,   
  offer "Remove this reference" (pairs with a diagnostic).                               
  5. Convert legacy csproj → SDK-style — single-shot rewrite for old <Project               
  ToolsVersion="..."> files. Huge win on legacy repos.                
                                                                                            
  Warm — frequently useful                                            
  6. Move PackageReference to Central Package Management — cut Version= here, add/update    
  <PackageVersion> in nearest Directory.Packages.props, set                                 
  ManagePackageVersionsCentrally=true if missing.
  7. Promote property to Directory.Build.props — for a property that's set the same way in  
  many csprojs, lift it once at the directory level.                                        
  8. Add missing TargetFramework — on a project root with neither TargetFramework nor
  TargetFrameworks set, offer to insert a sensible default (latest LTS).                    
  9. Switch single ↔ multi-target — TargetFramework ↔ TargetFrameworks (semicolon-list), and
   vice-versa with one selected TFM.                                                        
  10. Toggle Nullable enable/disable and ImplicitUsings enable/disable — one-click flip.
  11. Add OutputType=Exe when project has Program.cs but no OutputType (paired with         
  diagnostic / hint).                                                                       
  12. Wrap selection in <Choose>/<When Condition="…"> — for conditional includes/properties.
                                                                                            
  Nice-to-have                                                        
  13. Inline $(Property) value / Extract value to property — refactor a literal string used 
  in multiple places into a <PropertyGroup> entry, or inline the property back where used   
  once.
  14. Add InternalsVisibleTo for sibling test project — when current project name is Foo and
   there's a sibling Foo.Tests, offer to insert <InternalsVisibleTo Include="Foo.Tests" />. 
  15. Convert relative <ProjectReference> to absolute / shortest-relative — normalize paths.
  16. Remove duplicate <PackageReference> — when the same package is referenced twice       
  (paired with diagnostic).                                                                 
  17. Change package source / pin to a specific version — for security advisories (later,   
  integrates with NuGet vuln data).                                                         
  18. Generate Directory.Packages.props from current project — bootstrap CPM for a repo.
