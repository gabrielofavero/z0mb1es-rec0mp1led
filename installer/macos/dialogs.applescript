on run argv
    set action to item 1 of argv
    try
        if action is "question" then
            display dialog (item 2 of argv) with title "Z0MB1ES" buttons {"Cancel", "Continue"} default button "Continue" cancel button "Cancel"
            return "yes"
        else if action is "optional-art" then
            display dialog (item 2 of argv) with title "Optional artwork" buttons {"Skip", "Import artwork"} default button "Skip" cancel button "Skip"
            return "yes"
        else if action is "error" then
            display alert "Z0MB1ES" message (item 2 of argv) as critical
            return "ok"
        else if action is "progress" then
            display notification (item 2 of argv) with title "Z0MB1ES import"
            return "ok"
        else if action is "xap" then
            return POSIX path of (choose file with prompt "Select Z0MB1ES (on teh ph0ne) — Windows Phone XAP (.xap)")
        else if action is "i-maed-360" then
            return POSIX path of (choose file with prompt "Select I MAED A GAM3 W1TH Z0MB1ES 1N IT!!! — Xbox 360 LIVE package")
        else if action is "time-viking-360" then
            return POSIX path of (choose file with prompt "Select Time Viking — Xbox 360 LIVE package (optional artwork)")
        else if action is "i-maed-steam" then
            return POSIX path of (choose folder with prompt "Select the I MAED A GAM3 W1TH Z0MB1ES 1N IT!!! Steam game folder")
        else if action is "photos" then
            set selectedFiles to choose file with prompt "Choose creator PNG or JPEG photos" of type {"public.png", "public.jpeg"} with multiple selections allowed
            set resultText to ""
            repeat with selectedFile in selectedFiles
                set resultText to resultText & (POSIX path of selectedFile) & linefeed
            end repeat
            return resultText
        else if action is "sources" then
            set selectedItems to choose from list {"Z0MB1ES phone XAP", "I MAED artwork", "Time Viking artwork"} with title "Reimport assets" with prompt "Select sources to replace or add. Saves remain intact." with multiple selections allowed
            if selectedItems is false then return ""
            set resultText to ""
            repeat with selectedItem in selectedItems
                set resultText to resultText & (selectedItem as text) & linefeed
            end repeat
            return resultText
        else if action is "normal-kind" then
            set selectedItems to choose from list {"Steam directory", "Xbox 360 LIVE package"} with title "I MAED source" with prompt "Choose your I MAED A GAM3 W1TH Z0MB1ES 1N IT!!! source."
            if selectedItems is false then return ""
            return item 1 of selectedItems
        end if
        error "Unknown dialog action"
    on error number -128
        return "__CANCELLED__"
    end try
end run
