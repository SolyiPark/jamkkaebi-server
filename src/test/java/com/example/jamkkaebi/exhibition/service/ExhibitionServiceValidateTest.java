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
}
