using FluentValidation;
using Delivery_System.Models;

namespace Delivery_System.Validators
{
    public class OrderValidator : AbstractValidator<TblOrder>
    {
        public OrderValidator()
        {
            RuleFor(x => x.ItemName)
                .NotEmpty().WithMessage("Tên hàng hóa không được để trống")
                .MaximumLength(250).WithMessage("Tên hàng không được quá 250 ký tự");

            RuleFor(x => x.SendStation)
                .NotEmpty().WithMessage("Vui lòng chọn trạm gửi");

            RuleFor(x => x.ReceiveStation)
                .NotEmpty().WithMessage("Vui lòng chọn trạm nhận");

            RuleFor(x => x.SenderPhone)
                .NotEmpty().WithMessage("Số điện thoại người gửi không được trống")
                .Matches(@"^\d{10,11}$").WithMessage("Số điện thoại phải có 10-11 chữ số");

            RuleFor(x => x.ReceiverPhone)
                .NotEmpty().WithMessage("Số điện thoại người nhận không được trống")
                .Matches(@"^\d{10,11}$").WithMessage("Số điện thoại phải có 10-11 chữ số");

            RuleFor(x => x.Tr)
                .GreaterThanOrEqualTo(0).WithMessage("Phí TR không được nhỏ hơn 0");

            RuleFor(x => x.Ct)
                .GreaterThanOrEqualTo(0).WithMessage("Phí CT không được nhỏ hơn 0");
        }
    }
}
