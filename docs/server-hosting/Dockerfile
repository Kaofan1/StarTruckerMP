FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY ./StarTruckerMP-server .
EXPOSE 7777/tcp
EXPOSE 7777/udp
ENTRYPOINT ["dotnet", "StarTruckerMP.Server.dll"]