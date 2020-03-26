# RawConverter DotNet

## Description
Rawconverter ported to use dotnetcore and use new Thermo Dot Net Libraries. Wine is no longer required for use in linux. Developed for Centos 7 and Windows x64 machines. 

## Requirements
  * DotNetCore 3.1

## Install DotNetCore
```
sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm

sudo yum install dotnet-sdk-3.1
```

## Run
```
RawConverterDotnetCore test_raw/FLI_180508_JMK_Biognosys_new_prep_Brain_TMT10_10fr_HpH_fr10.raw --ms1 --ms2 --ms3

```
