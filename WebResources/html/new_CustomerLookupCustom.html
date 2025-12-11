<html><head>
    <meta charset="UTF-8">
    <title>Customer Search</title>
    <style>
        body {
            font-family: Arial, sans-serif;
            font-size: 13px;
            margin: 0;
            padding: 4px;
        }
        .search-container {
            position: relative;
            width: 100%;
        }
        #customerSearch {
            width: 98%;
            padding: 6px;
            border: 1px solid #ccc;
            border-radius: 4px;
        }
        .dropdown {
            position: absolute;
            top: 34px;
            left: 0;
            right: 0;
            border: 1px solid #ccc;
            border-radius: 4px;
            background: #fff;
            z-index: 9999;
            max-height: 150px;
            overflow-y: auto;
            display: none;
        }
        .section-label {
            font-weight: bold;
            background: #f4f4f4;
            padding: 4px;
        }
        .dropdown div {
            padding: 6px;
            cursor: pointer;
            display: flex;
            align-items: center;
            gap: 6px;
        }
        .dropdown div:hover {
            background: #f0f0f0;
        }
        .icon {
            width: 16px;
            height: 16px;
        }
    </style>
<meta></head>
<body data-new-gr-c-s-check-loaded="14.1265.0" data-gr-ext-installed="" data-new-gr-c-s-loaded="14.1265.0" style="overflow-wrap: break-word;">
    <div class="search-container">
        <input type="text" id="customerSearch" placeholder="Search customer (Account/Contact)">
        <div id="dropdown" class="dropdown"></div>
    </div>

    <script src="ClientGlobalContext.js.aspx"></script>
    <script>
        var dropdown = document.getElementById('dropdown');
        var searchBox = document.getElementById('customerSearch');
        var searchTimeout;

        searchBox.addEventListener('input', function () {
            var query = this.value.trim();
            if (query.length < 2) {
                dropdown.style.display = 'none';
                return;
            }
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(function () {
                searchCustomers(query);
            }, 400);
        });

        function searchCustomers(query) {
            dropdown.innerHTML = "<div>Searching...</div>";
            dropdown.style.display = 'block';

            var accountFetch = `
                <fetch top="5">
                    <entity name="account">
                        <attribute name="name" />
                        <attribute name="accountid" />
                        <filter>
                            <condition attribute="name" operator="like" value='%${query}%' />
                        </filter>
                        <order attribute="name" />
                    </entity>
                </fetch>`;

            var contactFetch = `
                <fetch top="5">
                    <entity name="contact">
                        <attribute name="fullname" />
                        <attribute name="contactid" />
                        <filter>
                            <condition attribute="fullname" operator="like" value='%${query}%' />
                        </filter>
                        <order attribute="fullname" />
                    </entity>
                </fetch>`;

            var encodedAccountFetch = encodeURIComponent(accountFetch);
            var encodedContactFetch = encodeURIComponent(contactFetch);

            Promise.all([
                retrieveRecords(encodedAccountFetch, "accounts"),
                retrieveRecords(encodedContactFetch, "contacts")
            ])
            .then(function (results) {
                var accounts = results[0];
                var contacts = results[1];

                dropdown.innerHTML = "";

                if (accounts.length === 0 && contacts.length === 0) {
                    dropdown.innerHTML = "<div>No records found</div>";
                    return;
                }

                if (accounts.length > 0) {
                    var accountHeader = document.createElement('div');
                    accountHeader.className = 'section-label';
                    accountHeader.textContent = "Accounts";
                    dropdown.appendChild(accountHeader);

                    accounts.forEach(function (acc) {
                        var item = document.createElement('div');
                        item.innerHTML = `<img class="icon" src="/_imgs/entity/ico_16_1.gif" /> ${acc.name}`;
                        item.onclick = function () {
                            setCustomerLookup(acc.accountid, acc.name, "account");
                        };
                        dropdown.appendChild(item);
                    });
                }

                if (contacts.length > 0) {
                    var contactHeader = document.createElement('div');
                    contactHeader.className = 'section-label';
                    contactHeader.textContent = "Contacts";
                    dropdown.appendChild(contactHeader);

                    contacts.forEach(function (con) {
                        var item = document.createElement('div');
                        item.innerHTML = `<img class="icon" src="/_imgs/entity/ico_16_2.gif" /> ${con.fullname}`;
                        item.onclick = function () {
                            setCustomerLookup(con.contactid, con.fullname, "contact");
                        };
                        dropdown.appendChild(item);
                    });
                }
            })
            .catch(function (error) {
                console.error("Error fetching records: ", error);
                dropdown.innerHTML = "<div>Error loading data</div>";
            });
        }

        function retrieveRecords(encodedFetch, entityPlural) {
            return new Promise(function (resolve, reject) {
                var req = new XMLHttpRequest();
                var url = window.parent.Xrm.Page.context.getClientUrl() + "/api/data/v9.0/" + entityPlural + "?fetchXml=" + encodedFetch;
                req.open("GET", url, true);
                req.setRequestHeader("OData-MaxVersion", "4.0");
                req.setRequestHeader("OData-Version", "4.0");
                req.setRequestHeader("Accept", "application/json");
                req.setRequestHeader("Content-Type", "application/json; charset=utf-8");
                req.onreadystatechange = function () {
                    if (this.readyState === 4) {
                        req.onreadystatechange = null;
                        if (this.status === 200) {
                            var results = JSON.parse(this.response);
                            resolve(results.value);
                        } else {
                            reject(this.statusText);
                        }
                    }
                };
                req.send();
            });
        }

        function setCustomerLookup(id, name, entityType) {
            console.log("Setting Customer lookup: ", name);

            var lookupValue = [{
                id: id,
                name: name,
                entityType: entityType
            }];

            if (window.parent.Xrm && window.parent.Xrm.Page) {
                var formContext = window.parent.Xrm.Page;
                formContext.getAttribute("customerid").setValue(lookupValue);
                formContext.getAttribute("customerid").setSubmitMode("always");
                console.log("✅ Customer lookup updated successfully.");
            }

            searchBox.value = name;
            dropdown.style.display = 'none';
        }
    </script>


</body><grammarly-desktop-integration data-grammarly-shadow-root="true"></grammarly-desktop-integration></html>