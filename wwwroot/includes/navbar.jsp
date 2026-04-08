<%-- 
    navbar.jsp - Liquid Glass Blue Version
--%>
<%@page import="dto.UserDTO"%>
<%@page contentType="text/html" pageEncoding="UTF-8"%>
<link rel="stylesheet" href="${pageContext.request.contextPath}/includes/navbar.css">

<%
    UserDTO navUser = (UserDTO) session.getAttribute("LOGIN_USER");
    String navRole = (String) session.getAttribute("ROLE");
    
    String fullName = (navUser != null) ? navUser.getFullName() : "Người dùng";
    String email = (navUser != null && navUser.getEmail() != null && !navUser.getEmail().isEmpty()) 
                   ? navUser.getEmail() : "Chưa cập nhật email";
    String initial = (!fullName.isEmpty()) ? fullName.substring(0, 1).toUpperCase() : "U";
    
    String currentURI = request.getRequestURI();
%>

<nav class="navbar">
    <a href="MainController?GoHome=true" class="nav-brand">
        <span class="brand-logo">🚚</span>
        <span class="company-name">Delivery System</span>
    </a>

    <div class="nav-menu-container">
        <div class="nav-indicator" id="navIndicator"></div>
        <ul class="nav-links" id="navLinks">
            <li><a href="MainController?GoHome=true" class="<%= currentURI.contains("home.jsp") ? "active" : "" %>">Trang chủ</a></li>
            <li><a href="MainController?ViewGoods=true" class="<%= currentURI.contains("goods.jsp") || currentURI.contains("list_order.jsp") || currentURI.contains("trash_order.jsp") ? "active" : "" %>">Hàng hóa</a></li>
            <li><a href="MainController?ViewReports=true" class="<%= currentURI.contains("report.jsp") ? "active" : "" %>">Báo cáo</a></li>
            <% if ("AD".equals(navRole)) { %>
                <li><a href="MainController?AdminPanel=true" class="<%= currentURI.contains("admin.jsp") ? "active" : "" %>">Quản trị</a></li>
            <% } %>
            <li><a href="MainController?ViewAbout=true" class="<%= currentURI.contains("about.jsp") ? "active" : "" %>">Hướng dẫn</a></li>
        </ul>
    </div>

    <div class="nav-user-zone" id="userZone">
        <div class="user-trigger" onclick="toggleUserDropdown(event)">
            <div class="user-avatar-small"><%= initial %></div>
            <span class="user-trigger-name"><%= fullName %></span>
        </div>

        <div class="dropdown-menu" id="userDropdown">
            <div class="dropdown-header">
                <span class="d-name"><%= fullName %></span>
                <span class="d-email"><%= email %></span>
            </div>
            <div class="dropdown-body">
                <form action="MainController" method="POST" style="margin: 0;">
                    <input type="hidden" name="csrfToken" value="${sessionScope.CSRF_TOKEN}">
                    <button type="submit" name="Logout" class="dropdown-item" style="width:100%; border:none; background:none; cursor:pointer; color:#e53e3e;">
                        🚪 Đăng xuất hệ thống
                    </button>
                </form>
            </div>
        </div>
    </div>
</nav>

<script>
    const navLinks = document.querySelectorAll('.nav-links li a');
    const indicator = document.getElementById('navIndicator');
    const menuContainer = document.querySelector('.nav-menu-container');

    function moveIndicator(element) {
        if (!element) return;
        
        // Tính toán khoảng cách offset chính xác
        const containerPadding = 6; // Khớp với padding trong CSS
        indicator.style.width = element.offsetWidth + 'px';
        indicator.style.left = (element.offsetLeft + containerPadding) + 'px';
    }

    // Khởi tạo ngay khi DOM sẵn sàng
    window.addEventListener('load', () => {
        const activeLink = document.querySelector('.nav-links li a.active');
        if (activeLink) {
            moveIndicator(activeLink);
        } else {
            // Mặc định cho mục đầu tiên nếu không tìm thấy active
            moveIndicator(navLinks[0]);
        }
    });

    navLinks.forEach(link => {
        link.addEventListener('mouseenter', (e) => moveIndicator(e.target));
    });

    menuContainer.addEventListener('mouseleave', () => {
        const currentActive = document.querySelector('.nav-links li a.active');
        if (currentActive) {
            moveIndicator(currentActive);
        }
    });

    function toggleUserDropdown(event) {
        event.stopPropagation();
        document.getElementById('userDropdown').classList.toggle('show');
    }

    window.onclick = function(event) {
        const dropdown = document.getElementById('userDropdown');
        if (dropdown && dropdown.classList.contains('show')) {
            if (!document.getElementById('userZone').contains(event.target)) {
                dropdown.classList.remove('show');
            }
        }
    }
</script>
