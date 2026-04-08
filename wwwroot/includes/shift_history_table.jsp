<%@page contentType="text/html" pageEncoding="UTF-8"%>
<%@ taglib prefix="c" uri="http://java.sun.com/jsp/jstl/core" %>
<%@ taglib prefix="fmt" uri="http://java.sun.com/jsp/jstl/fmt" %>

<c:choose>
    <c:when test="${not empty SHIFT_HISTORY}">
        <div class="table-responsive">
            <table class="table table-hover align-middle shadow-sm bg-white rounded">
                <thead class="table-dark">
                    <tr>
                        <th>Mã Ca</th>
                        <th>Bắt đầu</th>
                        <th>Kết thúc</th>
                        <th>Trạng thái</th>
                        <th>Hành động</th>
                    </tr>
                </thead>
                <tbody>
                    <c:forEach var="s" items="${SHIFT_HISTORY}">
                        <tr class="${s.shiftID == VIEW_SHIFT_ID ? 'table-info' : ''}">
                            <td><strong>#${s.shiftID}</strong></td>
                            <td>${s.startTime}</td>
                            <td>${s.endTime != null ? s.endTime : '---'}</td>
                            <td>
                                <span class="badge ${s.status eq 'ACTIVE' ? 'bg-success' : 'bg-secondary'}">
                                    ${s.status eq 'ACTIVE' ? 'Đang hoạt động' : 'Đã đóng'}
                                </span>
                            </td>
                            <td>
                                <a href="ReportController?viewShiftID=${s.shiftID}&targetStaffID=${not empty SELECTED_STAFF_ID ? SELECTED_STAFF_ID : s.staffID}" 
                                   class="btn btn-sm ${s.shiftID == VIEW_SHIFT_ID ? 'btn-primary' : 'btn-outline-primary'}">
                                   ${s.shiftID == VIEW_SHIFT_ID ? 'Đang xem' : 'Chi tiết'}
                                </a>
                            </td>
                        </tr>
                    </c:forEach>
                </tbody>
            </table>
        </div>
    </c:when>
    <c:otherwise>
        <div class="alert alert-light text-center border">
            <p class="mb-0 text-muted">Không có dữ liệu ca làm việc nào để hiển thị.</p>
        </div>
    </c:otherwise>
</c:choose>

<%-- Vùng hiển thị chi tiết đơn hàng khi bấm nút --%>
<c:if test="${not empty SHIFT_ORDERS}">
    <div class="mt-4 p-3 border-start border-primary border-4 bg-white shadow-sm rounded">
        <h6 class="fw-bold mb-3 text-primary d-flex justify-content-between">
            <span>📦 DANH SÁCH ĐƠN HÀNG TRONG CA #${VIEW_SHIFT_ID}</span>
            <span class="badge bg-primary">${SHIFT_ORDERS.size()} đơn</span>
        </h6>
        <div class="table-responsive">
            <table class="table table-sm table-bordered table-hover">
                <thead class="table-light text-secondary">
                    <tr>
                        <th>Mã Đơn</th>
                        <th>Tên Hàng</th>
                        <th>Người Nhận</th>
                        <th class="text-end">Cước</th>
                        <th class="text-center">Trạng Thái</th>
                    </tr>
                </thead>
                <tbody>
                    <c:forEach var="o" items="${SHIFT_ORDERS}">
                        <tr>
                            <td><code class="fw-bold">${o.orderID}</code></td>
                            <td>${o.itemName}</td>
                            <td>${o.receiverName}</td>
                            <td class="text-end text-success fw-bold"><fmt:formatNumber value="${o.amount}" type="number"/> K</td>
                            <td class="text-center">
                                <span class="badge ${o.shipStatus eq 'Đã Chuyển' ? 'bg-success' : 'bg-warning'}">
                                    ${o.shipStatus}
                                </span>
                            </td>
                        </tr>
                    </c:forEach>
                </tbody>
            </table>
        </div>
    </div>
</c:if>
