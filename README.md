
# TcPlugin.AzureBlob

This is a Total Commander FileSystem `.wfx` plugin!<br>
This plugin brings support for Azure BlockBlobs to Total Commander.<br>
It Supports only `BlockBlobs`!

![Image](https://raw.githubusercontent.com/r-Larch/TcPlugin.AzureBlob/master/Images/screenshot.jpg)

## Installation 

**There is no stable release yet!!**

To install the Plugin, using integrated plugin installer, do the following:
 * Download the latest Release [**FsAzureStorage.zip**](https://github.com/r-Larch/TcBuild/releases)
 * Use Total Commander to navigate to the zip-file and then hit `ENTER` on it.
 * Wait for the installer promt.
 * Follow the instructions of the installer and find the plugin under **Network Neighborhood**

More abaut the **[Total Commander integrated plugin installer](https://www.ghisler.ch/wiki/index.php/Plugin#Installation_using_Total_Commander.27s_integrated_plugin_installer).**


## How to build

It uses the **[TcBuild](https://github.com/r-Larch/TcBuild)** nuget package to build a Plugin 
that can be used with Total Commander.

## Enable Trace logging

To enable trace logging copy this file **[Totalcmd.exe.config](https://github.com/r-Larch/TcBuild/blob/master/Totalcmd.exe.config)**
into the directory of **Totalcmd.exe**.
In case you use the 64-bit version of Total Comander then rename the file to **Totalcmd64.exe.config**.
