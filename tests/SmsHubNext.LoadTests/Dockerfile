FROM mcr.microsoft.com/dotnet/aspnet:10.0.9
WORKDIR /app
COPY tests/SmsHubNext.LoadTests/.publish/ .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmsHubNext.dll"]
