# Spider
Simple web spider for downloading files.

See the `config.xml` file for how to configure execution.

Below is an example config to download 4K nature photos from a desktop wallpapers site:
```
<?xml version="1.0" encoding="utf-8" ?>
<config>
  <!-- Depth of page links to follow (including the starting URI's below). -->
  <maxdepth>2</maxdepth>
  <!-- Output path for downloaded files. -->
  <output>wallpaper</output>
  <uris>
    <!-- URI's to spider. -->
    <uri>https://backiee.com/categories/nature?4k=yes</uri>
  </uris>
  <exts>
    <!-- File extensions to download files for. -->
    <ext>jpg</ext>
    <ext>jpeg</ext>
  </exts>
  <filters>
    <!-- Regular expression filters for excluding or including links based on expression matches.
         The exclude expression must not match and the include expression must match in order for a link/file to be processed. -->
    <exclude></exclude>
    <include>/wallpaper/|/wallpapers/3840x2160</include>
  </filters>
</config>
```
