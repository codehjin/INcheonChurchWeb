if (typeof window.downloadExcelFile !== 'function') {
    window.downloadExcelFile = (fileName, base64String) => {
        const link = document.createElement('a');
        link.download = fileName;
        link.href = `data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,${base64String}`;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };
}

window.printPopup = (htmlContent) => {
    const printWindow = window.open('', '_blank', 'width=1000,height=800');
    printWindow.document.write('<html><head><title>인쇄</title>');
    printWindow.document.write('<style>@page { size: auto; margin: 10mm; } body { margin: 0; padding: 20px; }</style>');
    // 현재 페이지의 모든 스타일시트를 복사하여 디자인 유지
    Array.from(document.styleSheets).forEach(ss => {
        try {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = ss.href;
            printWindow.document.head.appendChild(link);
        } catch (e) {}
    });
    printWindow.document.write('</head><body>');
    printWindow.document.write(htmlContent);
    printWindow.document.write('</body></html>');
    printWindow.document.close();
    printWindow.focus();
    setTimeout(() => { printWindow.print(); printWindow.close(); }, 500);
};
