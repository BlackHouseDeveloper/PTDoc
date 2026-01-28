window.ptdocAuth = {
  submitLogin: (username, pin, returnUrl) => {
    const form = document.createElement("form");
    form.method = "post";
    form.action = "/auth/login";

    const fields = {
      username: username || "",
      pin: pin || "",
      returnUrl: returnUrl || "/"
    };

    Object.entries(fields).forEach(([name, value]) => {
      const input = document.createElement("input");
      input.type = "hidden";
      input.name = name;
      input.value = value;
      form.appendChild(input);
    });

    document.body.appendChild(form);
    form.submit();
  }
};
