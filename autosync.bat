:loop
git add .
git commit -m "auto update"
git push
timeout /t 10 >nul
goto loop