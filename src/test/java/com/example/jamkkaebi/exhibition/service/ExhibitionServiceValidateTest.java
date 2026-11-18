package com.example.jamkkaebi.exhibition.service;

import com.example.jamkkaebi.exhibition.config.ExhibitionProperties;
import com.example.jamkkaebi.exhibition.domain.ExhibitItemType;
import com.example.jamkkaebi.exhibition.dto.request.ExhibitionLayoutRequest;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Locale;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.catchThrowableOfType;

/**
 * 배치 저장의 형식 검사 — 그리드가 항목보다 작을 때. 형식 검사는 저장소를 읽기 전에 돌아서 그리드 설정만 넣고 본다.
 */
class ExhibitionServiceValidateTest {

    private static ExhibitionService service(int width, int height) {
        return new ExhibitionService(null, null, null, null, null, null, null,
                new ExhibitionProperties(width, height), null, null);
    }

    private static List<FieldError> errorsOf(ExhibitionService service, ExhibitionLayoutRequest.Item item) {
        BusinessException e = catchThrowableOfType(BusinessException.class,
                () -> service.saveLayout("any", List.of(item)));
        assertThat(e).isNotNull();
        assertThat(e.getErrorCode()).isEqualTo(ErrorCode.INVALID_INPUT);
        return e.getErrors();
    }

    @Test
    @DisplayName("1×7 그리드에는 2×2 건물을 놓을 수 없다고 알린다 (좌표 사유 '0 이상 0 미만'을 내지 않는다)")
    void 그리드보다_큰_건물() {
        List<FieldError> errors = errorsOf(service(1, 7), new ExhibitionLayoutRequest.Item(ExhibitItemType.BUILDING, 1, 0, 0));

        assertThat(errors).extracting(FieldError::getField).containsExactly("items[0]");
        assertThat(errors.get(0).getReason()).isEqualTo("1×7 그리드에는 2×2 칸을 차지하는 항목을 놓을 수 없습니다.");
    }

    @Test
    @DisplayName("세로만 모자라도 놓을 수 없다고 알린다")
    void 세로가_모자란_그리드() {
        List<FieldError> errors = errorsOf(service(7, 1), new ExhibitionLayoutRequest.Item(ExhibitItemType.BUILDING, 1, 3, 0));

        assertThat(errors).extracting(FieldError::getField).containsExactly("items[0]");
    }

    @Test
    @DisplayName("건물이 들어가는 그리드에서는 좌표 사유를 그대로 낸다")
    void 들어가는_그리드의_좌표_사유() {
        List<FieldError> errors = errorsOf(service(7, 7), new ExhibitionLayoutRequest.Item(ExhibitItemType.BUILDING, 1, 6, 0));

        assertThat(errors).extracting(FieldError::getField).containsExactly("items[0].x");
        assertThat(errors.get(0).getReason()).isEqualTo("0 이상 6 미만이어야 합니다.");
    }

    @Test
    @DisplayName("필드 경로와 사유의 숫자는 JVM 로캘과 상관없이 아라비아 숫자로 적는다")
    void 로캘과_상관없는_숫자() {
        // 숫자를 적는 데 쓰는 FORMAT 로캘만 바꾸고 되돌린다. Locale.setDefault(Locale) 는 일반·DISPLAY·FORMAT 을 모두 바꿔, 되돌릴 때 따로 정해 둔 FORMAT 로캘을 잃는다
        Locale saved = Locale.getDefault(Locale.Category.FORMAT);
        try {
            // 타이 숫자를 쓰는 로캘: String.formatted 를 쓰면 'items[๐]', '๗×๑' 로 적힌다
            Locale.setDefault(Locale.Category.FORMAT, Locale.forLanguageTag("th-TH-u-nu-thai"));
            List<FieldError> tooSmall = errorsOf(service(7, 1), new ExhibitionLayoutRequest.Item(ExhibitItemType.BUILDING, 1, 0, 0));
            List<FieldError> outside = errorsOf(service(7, 7), new ExhibitionLayoutRequest.Item(ExhibitItemType.BUILDING, 1, 6, 0));

            assertThat(tooSmall).extracting(FieldError::getField).containsExactly("items[0]");
            assertThat(tooSmall.get(0).getReason()).isEqualTo("7×1 그리드에는 2×2 칸을 차지하는 항목을 놓을 수 없습니다.");
            assertThat(outside).extracting(FieldError::getField).containsExactly("items[0].x");
            assertThat(outside.get(0).getReason()).isEqualTo("0 이상 6 미만이어야 합니다.");
        } finally {
            Locale.setDefault(Locale.Category.FORMAT, saved);
        }
    }
}
