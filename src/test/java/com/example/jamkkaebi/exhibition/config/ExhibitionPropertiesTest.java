package com.example.jamkkaebi.exhibition.config;

import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;
import org.springframework.boot.context.properties.bind.Binder;
import org.springframework.boot.context.properties.source.ConfigurationPropertySources;
import org.springframework.boot.context.properties.source.MapConfigurationPropertySource;
import org.springframework.boot.env.YamlPropertySourceLoader;
import org.springframework.core.env.PropertySource;
import org.springframework.core.io.FileSystemResource;

import java.util.List;
import java.util.Map;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * 전시관 그리드 설정 — 기본값, 덮어쓰기, 잘못된 값이 실제로 설정 파일에서 먹히는지.
 */
class ExhibitionPropertiesTest {

    @Test
    @DisplayName("설정이 없으면 7×7 을 쓴다")
    void 기본값() {
        ExhibitionProperties properties = new ExhibitionProperties(null, null);

        assertThat(properties.grid().width()).isEqualTo(7);
        assertThat(properties.grid().height()).isEqualTo(7);
    }

    @Test
    @DisplayName("가로와 세로를 따로 덮어쓸 수 있다")
    void 덮어쓰기() {
        ExhibitionProperties properties = bind(new MapConfigurationPropertySource(Map.of(
                "app.exhibition.grid-width", "12",
                "app.exhibition.grid-height", "8")));

        assertThat(properties.grid().width()).isEqualTo(12);
        assertThat(properties.grid().height()).isEqualTo(8);
    }

    @Test
    @DisplayName("0 이하는 어떤 좌표도 둘 수 없게 만들므로 기동에서 거절한다")
    void 잘못된_값() {
        assertThatThrownBy(() -> new ExhibitionProperties(0, 7)).isInstanceOf(IllegalArgumentException.class);
        assertThatThrownBy(() -> new ExhibitionProperties(7, -1)).isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    @DisplayName("application.yml 의 grid-width · grid-height 키가 실제로 바인딩된다")
    void 설정_파일_바인딩() throws Exception {
        // 클래스패스의 application.yml 은 테스트용 파일이 가리므로 운영 설정 파일을 경로로 읽는다.
        List<PropertySource<?>> yaml = new YamlPropertySourceLoader()
                .load("application", new FileSystemResource("src/main/resources/application.yml"));
        Binder binder = new Binder(ConfigurationPropertySources.from(yaml));

        // 기본값과 같은 값을 적어 두었으므로, 키 이름이 틀려도 기본값 덕에 통과하지 않게 원본 값을 직접 읽는다.
        assertThat(binder.bind("app.exhibition.grid-width", Integer.class).get()).isEqualTo(7);
        assertThat(binder.bind("app.exhibition.grid-height", Integer.class).get()).isEqualTo(7);
        assertThat(binder.bind("app.exhibition", ExhibitionProperties.class).get().grid().width()).isEqualTo(7);
    }

    private ExhibitionProperties bind(MapConfigurationPropertySource source) {
        return new Binder(source).bind("app.exhibition", ExhibitionProperties.class).get();
    }
}
